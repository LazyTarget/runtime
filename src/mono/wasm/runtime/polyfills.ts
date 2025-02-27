import Configuration from "consts:configuration";
import MonoWasmThreads from "consts:monoWasmThreads";
import { ENVIRONMENT_IS_ESM, ENVIRONMENT_IS_NODE, ENVIRONMENT_IS_SHELL, ENVIRONMENT_IS_WEB, ENVIRONMENT_IS_WORKER, INTERNAL, Module, runtimeHelpers } from "./imports";
import { afterUpdateGlobalBufferAndViews } from "./memory";
import { afterLoadWasmModuleToWorker } from "./pthreads/browser";
import { afterThreadInitTLS } from "./pthreads/worker";
import { DotnetModuleConfigImports, EarlyReplacements } from "./types";

let node_fs: any | undefined = undefined;
let node_url: any | undefined = undefined;

export function init_polyfills(replacements: EarlyReplacements): void {
    const anyModule = Module as any;

    // performance.now() is used by emscripten and doesn't work in JSC
    if (typeof globalThis.performance === "undefined") {
        globalThis.performance = dummyPerformance as any;
    }
    if (typeof globalThis.URL === "undefined") {
        globalThis.URL = class URL {
            private url;
            constructor(url: string) {
                this.url = url;
            }
            toString() {
                return this.url;
            }
        } as any;
    }
    // v8 shell doesn't have Event and EventTarget
    if (MonoWasmThreads && typeof globalThis.Event === "undefined") {
        globalThis.Event = class Event {
            readonly type: string;
            constructor(type: string) {
                this.type = type;
            }
        } as any;
    }
    if (MonoWasmThreads && typeof globalThis.EventTarget === "undefined") {
        globalThis.EventTarget = class EventTarget {
            private subscribers = new Map<string, Array<{ listener: EventListenerOrEventListenerObject, oneShot: boolean }>>();
            addEventListener(type: string, listener: EventListenerOrEventListenerObject | null, options?: boolean | AddEventListenerOptions) {
                if (listener === undefined || listener == null)
                    return;
                let oneShot = false;
                if (options !== undefined) {
                    for (const [k, v] of Object.entries(options)) {
                        if (k === "once") {
                            oneShot = v ? true : false;
                            continue;
                        }
                        throw new Error(`FIXME: addEventListener polyfill doesn't implement option '${k}'`);
                    }
                }
                if (!this.subscribers.has(type)) {
                    this.subscribers.set(type, []);
                }
                const listeners = this.subscribers.get(type);
                if (listeners === undefined) {
                    throw new Error("can't happen");
                }
                listeners.push({ listener, oneShot });
            }
            removeEventListener(type: string, listener: EventListenerOrEventListenerObject | null, options?: boolean | EventListenerOptions) {
                if (listener === undefined || listener == null)
                    return;
                if (options !== undefined) {
                    throw new Error("FIXME: removeEventListener polyfill doesn't implement options");
                }
                if (!this.subscribers.has(type)) {
                    return;
                }
                const subscribers = this.subscribers.get(type);
                if (subscribers === undefined)
                    return;
                let index = -1;
                const n = subscribers.length;
                for (let i = 0; i < n; ++i) {
                    if (subscribers[i].listener === listener) {
                        index = i;
                        break;
                    }
                }
                if (index > -1) {
                    subscribers.splice(index, 1);
                }
            }
            dispatchEvent(event: Event) {
                if (!this.subscribers.has(event.type)) {
                    return true;
                }
                let subscribers = this.subscribers.get(event.type);
                if (subscribers === undefined) {
                    return true;
                }
                let needsCopy = false;
                for (const sub of subscribers) {
                    if (sub.oneShot) {
                        needsCopy = true;
                        break;
                    }
                }
                if (needsCopy) {
                    subscribers = subscribers.slice(0);
                }
                for (const sub of subscribers) {
                    const listener = sub.listener;
                    if (sub.oneShot) {
                        this.removeEventListener(event.type, listener);
                    }
                    if (typeof listener === "function") {
                        listener.call(this, event);
                    } else {
                        listener.handleEvent(event);
                    }
                }
                return true;
            }
        };
    }

    // require replacement
    const imports = anyModule.imports = Module.imports || <DotnetModuleConfigImports>{};
    const requireWrapper = (wrappedRequire: Function) => (name: string) => {
        const resolved = (<any>Module.imports)[name];
        if (resolved) {
            return resolved;
        }
        return wrappedRequire(name);
    };
    if (imports.require) {
        runtimeHelpers.requirePromise = replacements.requirePromise = Promise.resolve(requireWrapper(imports.require));
    }
    else if (replacements.require) {
        runtimeHelpers.requirePromise = replacements.requirePromise = Promise.resolve(requireWrapper(replacements.require));
    } else if (replacements.requirePromise) {
        runtimeHelpers.requirePromise = replacements.requirePromise.then(require => requireWrapper(require));
    } else {
        runtimeHelpers.requirePromise = replacements.requirePromise = Promise.resolve(requireWrapper((name: string) => {
            throw new Error(`Please provide Module.imports.${name} or Module.imports.require`);
        }));
    }

    // script location
    runtimeHelpers.scriptDirectory = replacements.scriptDirectory = detectScriptDirectory(replacements);
    anyModule.mainScriptUrlOrBlob = replacements.scriptUrl;// this is needed by worker threads
    if (Configuration === "Debug") {
        console.debug(`MONO_WASM: starting script ${replacements.scriptUrl}`);
        console.debug(`MONO_WASM: starting in ${runtimeHelpers.scriptDirectory}`);
    }
    if (anyModule.__locateFile === anyModule.locateFile) {
        // above it's our early version from dotnet.es6.pre.js, we could replace it with better
        anyModule.locateFile = runtimeHelpers.locateFile = (path) => {
            if (isPathAbsolute(path)) return path;
            return runtimeHelpers.scriptDirectory + path;
        };
    } else {
        // we use what was given to us
        runtimeHelpers.locateFile = anyModule.locateFile;
    }

    // fetch poly
    if (imports.fetch) {
        replacements.fetch = runtimeHelpers.fetch_like = imports.fetch;
    }
    else {
        replacements.fetch = runtimeHelpers.fetch_like = fetch_like;
    }

    // misc
    replacements.noExitRuntime = ENVIRONMENT_IS_WEB;

    // threads
    if (MonoWasmThreads) {
        if (replacements.pthreadReplacements) {
            const originalLoadWasmModuleToWorker = replacements.pthreadReplacements.loadWasmModuleToWorker;
            replacements.pthreadReplacements.loadWasmModuleToWorker = (worker: Worker, onFinishedLoading: Function): void => {
                originalLoadWasmModuleToWorker(worker, onFinishedLoading);
                afterLoadWasmModuleToWorker(worker);
            };
            const originalThreadInitTLS = replacements.pthreadReplacements.threadInitTLS;
            replacements.pthreadReplacements.threadInitTLS = (): void => {
                originalThreadInitTLS();
                afterThreadInitTLS();
            };
        }
    }

    // memory
    const originalUpdateGlobalBufferAndViews = replacements.updateGlobalBufferAndViews;
    replacements.updateGlobalBufferAndViews = (buffer: ArrayBufferLike) => {
        originalUpdateGlobalBufferAndViews(buffer);
        afterUpdateGlobalBufferAndViews(buffer);
    };
}

export async function init_polyfills_async(): Promise<void> {
    if (ENVIRONMENT_IS_NODE && ENVIRONMENT_IS_ESM) {
        // wait for locateFile setup on NodeJs
        INTERNAL.require = await runtimeHelpers.requirePromise;
        if (globalThis.performance === dummyPerformance) {
            const { performance } = INTERNAL.require("perf_hooks");
            globalThis.performance = performance;
        }
    }
}

const dummyPerformance = {
    now: function () {
        return Date.now();
    }
};

export async function fetch_like(url: string, init?: RequestInit): Promise<Response> {
    try {
        if (ENVIRONMENT_IS_NODE) {
            if (!node_fs) {
                const node_require = await runtimeHelpers.requirePromise;
                node_url = node_require("url");
                node_fs = node_require("fs");
            }
            if (url.startsWith("file://")) {
                url = node_url.fileURLToPath(url);
            }

            const arrayBuffer = await node_fs.promises.readFile(url);
            return <Response><any>{
                ok: true,
                url,
                arrayBuffer: () => arrayBuffer,
                json: () => JSON.parse(arrayBuffer)
            };
        }
        else if (typeof (globalThis.fetch) === "function") {
            return globalThis.fetch(url, init || { credentials: "same-origin" });
        }
        else if (typeof (read) === "function") {
            // note that it can't open files with unicode names, like Stra<unicode char - Latin Small Letter Sharp S>e.xml
            // https://bugs.chromium.org/p/v8/issues/detail?id=12541
            const arrayBuffer = new Uint8Array(read(url, "binary"));
            return <Response><any>{
                ok: true,
                url,
                arrayBuffer: () => arrayBuffer,
                json: () => JSON.parse(Module.UTF8ArrayToString(arrayBuffer, 0, arrayBuffer.length))
            };
        }
    }
    catch (e: any) {
        return <Response><any>{
            ok: false,
            url,
            arrayBuffer: () => { throw e; },
            json: () => { throw e; }
        };
    }
    throw new Error("No fetch implementation available");
}

function normalizeFileUrl(filename: string) {
    // unix vs windows
    // remove query string
    return filename.replace(/\\/g, "/").replace(/[?#].*/, "");
}

function normalizeDirectoryUrl(dir: string) {
    return dir.slice(0, dir.lastIndexOf("/")) + "/";
}

export function detectScriptDirectory(replacements: EarlyReplacements): string {
    if (ENVIRONMENT_IS_WORKER) {
        // Check worker, not web, since window could be polyfilled
        replacements.scriptUrl = self.location.href;
    }
    // when ENVIRONMENT_IS_ESM we have scriptUrl from import.meta.url from dotnet.es6.lib.js
    if (!ENVIRONMENT_IS_ESM) {
        if (ENVIRONMENT_IS_WEB) {
            if (
                (typeof (globalThis.document) === "object") &&
                (typeof (globalThis.document.createElement) === "function")
            ) {
                // blazor injects a module preload link element for dotnet.[version].[sha].js
                const blazorDotNetJS = Array.from(document.head.getElementsByTagName("link")).filter(elt => elt.rel !== undefined && elt.rel == "modulepreload" && elt.href !== undefined && elt.href.indexOf("dotnet") != -1 && elt.href.indexOf(".js") != -1);
                if (blazorDotNetJS.length == 1) {
                    replacements.scriptUrl = blazorDotNetJS[0].href;
                } else {
                    const temp = globalThis.document.createElement("a");
                    temp.href = "dotnet.js";
                    replacements.scriptUrl = temp.href;
                }
            }
        }
        if (ENVIRONMENT_IS_NODE) {
            if (typeof __filename !== "undefined") {
                // unix vs windows
                replacements.scriptUrl = __filename;
            }
        }
    }
    if (!replacements.scriptUrl) {
        // probably V8 shell in non ES6
        replacements.scriptUrl = "./dotnet.js";
    }
    replacements.scriptUrl = normalizeFileUrl(replacements.scriptUrl);
    return normalizeDirectoryUrl(replacements.scriptUrl);
}

const protocolRx = /^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//;
const windowsAbsoluteRx = /[a-zA-Z]:[\\/]/;
function isPathAbsolute(path: string): boolean {
    if (ENVIRONMENT_IS_NODE || ENVIRONMENT_IS_SHELL) {
        // unix /x.json
        // windows \x.json
        // windows C:\x.json
        // windows C:/x.json
        return path.startsWith("/") || path.startsWith("\\") || path.indexOf("///") !== -1 || windowsAbsoluteRx.test(path);
    }

    // anything with protocol is always absolute
    // windows file:///C:/x.json
    // windows http://C:/x.json
    return protocolRx.test(path);
}