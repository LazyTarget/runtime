// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ComWrappersTests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using Xunit;

    static class WeakReferenceNative
    {
        [DllImport(nameof(WeakReferenceNative))]
        public static extern IntPtr CreateWeakReferencableObject();

        [DllImport(nameof(WeakReferenceNative))]
        public static extern IntPtr CreateAggregatedWeakReferenceObject(IntPtr outer);
    }

    public struct VtblPtr
    {
        public IntPtr Vtbl;
    }

    public enum WrapperRegistration
    {
        Local,
        TrackerSupport,
        Marshalling,
    }

    public unsafe class WeakReferenceableWrapper
    {
        private struct Vtbl
        {
            public delegate* unmanaged<IntPtr, Guid*, IntPtr*, int> QueryInterface;
            public delegate* unmanaged<IntPtr, int> AddRef;
            public delegate* unmanaged<IntPtr, int> Release;
        }

        private readonly IntPtr instance;
        private readonly Vtbl vtable;
        private readonly bool releaseInFinalizer;

        public WrapperRegistration Registration { get; }

        public WeakReferenceableWrapper(IntPtr instance, WrapperRegistration reg, bool releaseInFinalizer = true)
        {
            var inst = Marshal.PtrToStructure<VtblPtr>(instance);
            this.vtable = Marshal.PtrToStructure<Vtbl>(inst.Vtbl);
            this.instance = instance;
            this.releaseInFinalizer = releaseInFinalizer;
            Registration = reg;
        }

        public int QueryInterface(Guid iid, out IntPtr ptr)
        {
            fixed(IntPtr* ppv = &ptr)
            {
                return this.vtable.QueryInterface(this.instance, &iid, ppv);
            }
        }

        ~WeakReferenceableWrapper()
        {
            if (this.instance != IntPtr.Zero && this.releaseInFinalizer)
            {
                this.vtable.Release(this.instance);
            }
        }
    }

    class DerivedObject : ICustomQueryInterface
    {
        private WeakReferenceableWrapper inner;
        public DerivedObject(TestComWrappers comWrappersInstance)
        {
            IntPtr innerInstance = WeakReferenceNative.CreateAggregatedWeakReferenceObject(
                comWrappersInstance.GetOrCreateComInterfaceForObject(this, CreateComInterfaceFlags.None));
            inner = new WeakReferenceableWrapper(innerInstance, comWrappersInstance.Registration, releaseInFinalizer: false);
            comWrappersInstance.GetOrRegisterObjectForComInstance(innerInstance, CreateObjectFlags.Aggregation, this);
        }

        public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr ppv)
        {
            return inner.QueryInterface(iid, out ppv) == 0 ? CustomQueryInterfaceResult.Handled : CustomQueryInterfaceResult.Failed;
        }
    }

    class TestComWrappers : ComWrappers
    {
        public WrapperRegistration Registration { get; }

        public TestComWrappers(WrapperRegistration reg = WrapperRegistration.Local)
        {
            Registration = reg;
        }

        protected unsafe override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            count = 0;
            return null;
        }

        protected override object CreateObject(IntPtr externalComObject, CreateObjectFlags flag)
        {
            Marshal.AddRef(externalComObject);
            return new WeakReferenceableWrapper(externalComObject, Registration);
        }

        protected override void ReleaseObjects(IEnumerable objects)
        {
        }

        public static readonly TestComWrappers TrackerSupportInstance = new TestComWrappers(WrapperRegistration.TrackerSupport);
        public static readonly TestComWrappers MarshallingInstance = new TestComWrappers(WrapperRegistration.Marshalling);
    }

    class Program
    {

        private static void ValidateWeakReferenceState(WeakReference<WeakReferenceableWrapper> wr, bool expectedIsAlive, TestComWrappers sourceWrappers = null)
        {
            WeakReferenceableWrapper target;
            bool isAlive = wr.TryGetTarget(out target);
            Assert.Equal(expectedIsAlive, isAlive);

            if (isAlive && sourceWrappers != null)
                Assert.Equal(sourceWrappers.Registration, target.Registration);
        }

        private static (WeakReference<WeakReferenceableWrapper>, IntPtr) GetWeakReference(TestComWrappers cw)
        {
            IntPtr objRaw = WeakReferenceNative.CreateWeakReferencableObject();
            var obj = (WeakReferenceableWrapper)cw.GetOrCreateObjectForComInstance(objRaw, CreateObjectFlags.None);
            var wr = new WeakReference<WeakReferenceableWrapper>(obj);
            ValidateWeakReferenceState(wr, expectedIsAlive: true, cw);
            return (wr, objRaw);
        }

        private static IntPtr SetWeakReferenceTarget(WeakReference<WeakReferenceableWrapper> wr, TestComWrappers cw)
        {
            IntPtr objRaw = WeakReferenceNative.CreateWeakReferencableObject();
            var obj = (WeakReferenceableWrapper)cw.GetOrCreateObjectForComInstance(objRaw, CreateObjectFlags.None);
            wr.SetTarget(obj);
            ValidateWeakReferenceState(wr, expectedIsAlive: true, cw);
            return objRaw;
        }

        private static void ValidateNativeWeakReference(TestComWrappers cw)
        {
            Console.WriteLine($"  -- Validate weak reference creation");
            var (weakRef, nativeRef) = GetWeakReference(cw);

            // Make sure RCW is collected
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Non-globally registered ComWrappers instances do not support rehydration.
            // A weak reference to an RCW wrapping an IWeakReference can stay alive if the RCW was created through
            // a global ComWrappers instance. If the RCW was created through a local ComWrappers instance, the weak
            // reference should be dead and stay dead once the RCW is collected.
            bool supportsRehydration = cw.Registration != WrapperRegistration.Local;

            Console.WriteLine($"    -- Validate RCW recreation");
            ValidateWeakReferenceState(weakRef, expectedIsAlive: supportsRehydration, cw);

            // Release the last native reference.
            Marshal.Release(nativeRef);
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // After all native references die and the RCW is collected, the weak reference should be dead and stay dead.
            Console.WriteLine($"    -- Validate release");
            ValidateWeakReferenceState(weakRef, expectedIsAlive: false);

            // Reset the weak reference target
            Console.WriteLine($"  -- Validate target reset");
            nativeRef = SetWeakReferenceTarget(weakRef, cw);

            // Make sure RCW is collected
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Console.WriteLine($"    -- Validate RCW recreation");
            ValidateWeakReferenceState(weakRef, expectedIsAlive: supportsRehydration, cw);

            // Release the last native reference.
            Marshal.Release(nativeRef);
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // After all native references die and the RCW is collected, the weak reference should be dead and stay dead.
            Console.WriteLine($"    -- Validate release");
            ValidateWeakReferenceState(weakRef, expectedIsAlive: false);
        }

        static void ValidateGlobalInstanceTrackerSupport()
        {
            Console.WriteLine($"Running {nameof(ValidateGlobalInstanceTrackerSupport)}...");
            ValidateNativeWeakReference(TestComWrappers.TrackerSupportInstance);
        }

        static void ValidateGlobalInstanceMarshalling()
        {
            Console.WriteLine($"Running {nameof(ValidateGlobalInstanceMarshalling)}...");
            ValidateNativeWeakReference(TestComWrappers.MarshallingInstance);
        }

        static void ValidateLocalInstance()
        {
            Console.WriteLine($"Running {nameof(ValidateLocalInstance)}...");
            ValidateNativeWeakReference(new TestComWrappers());
        }

        static void ValidateNonComWrappers()
        {
            Console.WriteLine($"Running {nameof(ValidateNonComWrappers)}...");

            (WeakReference, IntPtr) GetWeakReference()
            {
                IntPtr objRaw = WeakReferenceNative.CreateWeakReferencableObject();
                var obj = Marshal.GetObjectForIUnknown(objRaw);
                return (new WeakReference(obj), objRaw);
            }

            bool HasTarget(WeakReference wr)
            {
                return wr.Target != null;
            }

            var (weakRef, nativeRef) = GetWeakReference();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // A weak reference to an RCW wrapping an IWeakReference created throguh the built-in system
            // should stay alive even after the RCW dies
            Assert.False(weakRef.IsAlive);
            Assert.True(HasTarget(weakRef));

            // Release the last native reference.
            Marshal.Release(nativeRef);
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // After all native references die and the RCW is collected, the weak reference should be dead and stay dead.
            Assert.Null(weakRef.Target);
        }

        static void ValidateAggregatedWeakReference()
        {
            Console.WriteLine("Validate weak reference with aggregation.");
            var (handle, weakRef) = GetWeakReference();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.Null(handle.Target);
            Assert.False(weakRef.TryGetTarget(out _));

            static (GCHandle handle, WeakReference<DerivedObject>) GetWeakReference()
            {
                DerivedObject obj = new DerivedObject(TestComWrappers.TrackerSupportInstance);
                // We use an explicit weak GC handle here to enable us to validate that we are using "weak" GCHandle
                // semantics with the weak reference.
                return (GCHandle.Alloc(obj, GCHandleType.Weak), new WeakReference<DerivedObject>(obj));
            }
        }

        static int Main(string[] doNotUse)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    ValidateNonComWrappers();

                    ComWrappers.RegisterForMarshalling(TestComWrappers.MarshallingInstance);
                    ValidateGlobalInstanceMarshalling();
                }

                ComWrappers.RegisterForTrackerSupport(TestComWrappers.TrackerSupportInstance);
                ValidateGlobalInstanceTrackerSupport();
                ValidateAggregatedWeakReference();

                ValidateLocalInstance();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Test Failure: {e}");
                return 101;
            }

            return 100;
        }
    }
}

