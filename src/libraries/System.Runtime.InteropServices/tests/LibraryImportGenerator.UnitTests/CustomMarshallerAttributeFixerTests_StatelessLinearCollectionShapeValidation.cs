// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Interop;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using static Microsoft.Interop.Analyzers.CustomMarshallerAttributeAnalyzer;

using VerifyCS = LibraryImportGenerator.UnitTests.Verifiers.CSharpCodeFixVerifier<
    Microsoft.Interop.Analyzers.CustomMarshallerAttributeAnalyzer,
    Microsoft.Interop.Analyzers.CustomMarshallerAttributeFixer>;

namespace LibraryImportGenerator.UnitTests
{
    [ActiveIssue("https://github.com/dotnet/runtime/issues/60650", TestRuntimes.Mono)]
    public class CustomMarshallerAttributeAnalyzerTests_StatelessLinearCollectionShapeValidation
    {
        [Fact]
        public async Task ModeThatUsesManagedToUnmanagedShape_Missing_AllMethods_ReportsDiagnostic()
        {
            string source = """
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedIn, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedOut, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementIn, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementIn, "ManagedType"));
        }

        [Fact]
        public async Task ModeThatUsesManagedToUnmanagedShape_Missing_ContainerMethods_ReportsDiagnostic()
        {
            string source = """
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedIn, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedOut, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementIn, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementIn, "ManagedType"));
        }

        [Fact]
        public async Task ModeThatUsesManagedToUnmanagedShape_Missing_GetManagedValuesSource_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedIn, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedOut, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementIn, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;

                    public static Span<byte> GetUnmanagedValuesDestination(nint unmanaged, int numElements) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementIn, "ManagedType"));
        }

        [Fact]
        public async Task ModeThatUsesManagedToUnmanagedShape_Missing_GetUnmanagedValuesDestination_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedIn, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedOut, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementIn, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;

                    public static Span<byte> GetManagedValuesSource(ManagedType m) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementIn, "ManagedType"));
        }

        [Fact]
        public async Task ModeThatUsesManagedToUnmanagedShape_MismatchedUnmanagedType_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedIn, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedOut, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementIn, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;

                    public static ReadOnlySpan<int> GetManagedValuesSource(ManagedType m) => default;

                    public static Span<T> GetUnmanagedValuesDestination(int unmanaged, int numElements) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(FirstParameterMustMatchReturnTypeRule).WithLocation(0).WithArguments("MarshallerType<T>.GetUnmanagedValuesDestination(int, int)", "MarshallerType<T>.AllocateContainerForUnmanagedElements(ManagedType, out int)"),
                VerifyCS.Diagnostic(FirstParameterMustMatchReturnTypeRule).WithLocation(1).WithArguments("MarshallerType<T>.GetUnmanagedValuesDestination(int, int)", "MarshallerType<T>.AllocateContainerForUnmanagedElements(ManagedType, out int)"),
                VerifyCS.Diagnostic(FirstParameterMustMatchReturnTypeRule).WithLocation(2).WithArguments("MarshallerType<T>.GetUnmanagedValuesDestination(int, int)", "MarshallerType<T>.AllocateContainerForUnmanagedElements(ManagedType, out int)"));
        }

        [Fact]
        public async Task ModeThatUsesManagedToUnmanagedShape_DoesNotReportDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedIn, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedOut, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementIn, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;

                    public static ReadOnlySpan<int> GetManagedValuesSource(ManagedType m) => default;

                    public static Span<T> GetUnmanagedValuesDestination(nint unmanaged, int numElements) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source);
        }


        [Fact]
        public async Task ModeThatUsesManagedToUnmanagedShape_InvalidCollectionElementType_DoesNotReportDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedIn, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedOut, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementIn, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;

                    public static ReadOnlySpan<int> GetManagedValuesSource(ManagedType m) => default;

                    public static Span<byte> GetUnmanagedValuesDestination(nint unmanaged, int numElements) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(ReturnTypeMustBeExpectedTypeRule).WithLocation(0).WithArguments("MarshallerType<T>.GetUnmanagedValuesDestination(nint, int)", "System.Span<T>"),
                VerifyCS.Diagnostic(ReturnTypeMustBeExpectedTypeRule).WithLocation(1).WithArguments("MarshallerType<T>.GetUnmanagedValuesDestination(nint, int)", "System.Span<T>"),
                VerifyCS.Diagnostic(ReturnTypeMustBeExpectedTypeRule).WithLocation(2).WithArguments("MarshallerType<T>.GetUnmanagedValuesDestination(nint, int)", "System.Span<T>"));
        }

        [Fact]
        public async Task ModeThatUsesUnmanagedToManagedShape_Missing_AllMethods_ReportsDiagnostic()
        {
            string source = """
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedOut, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedIn, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementOut, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementOut, "ManagedType"));
        }

        [Fact]
        public async Task ModeThatUsesUnmanagedToManagedShape_Missing_ContainerMethods_ReportsDiagnostic()
        {
            string source = """
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedOut, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedIn, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementOut, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static ManagedType AllocateContainerForManagedElements(nint m, int numElements) => throw null;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementOut, "ManagedType"));
        }

        [Fact]
        public async Task ModeThatUsesUnmanagedToManagedShape_Missing_GetUnmanagedValuesSource_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedOut, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedIn, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementOut, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static ManagedType AllocateContainerForManagedElements(nint m, int numElements) => throw null;

                    public static Span<byte> GetManagedValuesDestination(ManagedType m) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementOut, "ManagedType"));
        }

        [Fact]
        public async Task ModeThatUsesUnmanagedToManagedShape_Missing_GetManagedValuesDestination_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedOut, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedIn, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementOut, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static ManagedType AllocateContainerForManagedElements(nint unmanaged, out int numElements) => throw null;

                    public static Span<byte> GetUnmanagedValuesSource(nint unmanaged, int numElements) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(0).WithArguments("MarshallerType<T>", MarshalMode.ManagedToUnmanagedOut, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(1).WithArguments("MarshallerType<T>", MarshalMode.UnmanagedToManagedIn, "ManagedType"),
                VerifyCS.Diagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule).WithLocation(2).WithArguments("MarshallerType<T>", MarshalMode.ElementOut, "ManagedType"));
        }

        [Fact]
        public async Task ModeThatUsesUnmanagedToManagedShape_MismatchedUnmanagedType_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedOut, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedIn, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementOut, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static ManagedType AllocateContainerForManagedElements(nint unmanaged, out int numElements) => throw null;

                    public static ReadOnlySpan<T> GetUnmanagedValuesSource(int unmanaged, int numElements) => default;

                    public static Span<byte> GetManagedValuesDestination(ManagedType m) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(FirstParametersMustMatchRule).WithLocation(0).WithArguments("MarshallerType<T>.GetUnmanagedValuesSource(int, int)", "MarshallerType<T>.AllocateContainerForManagedElements(nint, out int)"),
                VerifyCS.Diagnostic(FirstParametersMustMatchRule).WithLocation(1).WithArguments("MarshallerType<T>.GetUnmanagedValuesSource(int, int)", "MarshallerType<T>.AllocateContainerForManagedElements(nint, out int)"),
                VerifyCS.Diagnostic(FirstParametersMustMatchRule).WithLocation(2).WithArguments("MarshallerType<T>.GetUnmanagedValuesSource(int, int)", "MarshallerType<T>.AllocateContainerForManagedElements(nint, out int)"));
        }

        [Fact]
        public async Task ModeThatUsesUnmanagedToManagedShape_DoesNotReportDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedOut, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedIn, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementOut, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static ManagedType AllocateContainerForManagedElements(nint unmanaged, out int numElements) => throw null;

                    public static ReadOnlySpan<T> GetUnmanagedValuesSource(nint unmanaged, int numElements) => default;

                    public static Span<byte> GetManagedValuesDestination(ManagedType m) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source);
        }

        [Fact]
        public async Task ModeThatUsesUnmanagedToManagedShape_InvalidCollectionElementType_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedOut, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedIn, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementOut, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static ManagedType AllocateContainerForManagedElements(nint unmanaged, out int numElements) => throw null;

                    public static ReadOnlySpan<int> GetUnmanagedValuesSource(nint unmanaged, int numElements) => default;

                    public static Span<byte> GetManagedValuesDestination(ManagedType m) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(ReturnTypeMustBeExpectedTypeRule).WithLocation(0).WithArguments("MarshallerType<T>.GetUnmanagedValuesSource(nint, int)", "System.ReadOnlySpan<T>"),
                VerifyCS.Diagnostic(ReturnTypeMustBeExpectedTypeRule).WithLocation(1).WithArguments("MarshallerType<T>.GetUnmanagedValuesSource(nint, int)", "System.ReadOnlySpan<T>"),
                VerifyCS.Diagnostic(ReturnTypeMustBeExpectedTypeRule).WithLocation(2).WithArguments("MarshallerType<T>.GetUnmanagedValuesSource(nint, int)", "System.ReadOnlySpan<T>"));
        }

        [Fact]
        public async Task CallerAllocatedBuffer_NoBufferSize_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedIn, typeof({|#0:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, Span<byte> b, out int numElements) => throw null;
                
                    public static ReadOnlySpan<int> GetManagedValuesSource(ManagedType m) => default;
                
                    public static Span<byte> GetUnmanagedValuesDestination(nint unmanaged, int numElements) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(
                source,
                VerifyCS.Diagnostic(StatelessLinearCollectionCallerAllocConstructorMustHaveBufferSizeRule).WithLocation(0).WithArguments("MarshallerType<T>", "byte"));
        }

        [Fact]
        public async Task ModeThatUsesBidirectionalShape_DoesNotReportDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedRef, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedRef, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementRef, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;
                
                    public static ReadOnlySpan<int> GetManagedValuesSource(ManagedType m) => default;
                
                    public static Span<T> GetUnmanagedValuesDestination(nint unmanaged, int numElements) => default;

                    public static ManagedType AllocateContainerForManagedElements(nint unmanaged, out int numElements) => throw null;
                
                    public static ReadOnlySpan<T> GetUnmanagedValuesSource(nint unmanaged, int numElements) => default;
                
                    public static Span<int> GetManagedValuesDestination(ManagedType m) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(source);
        }

        [Fact]
        public async Task ModeThatUsesBidirectionalShape_MismatchedManagedElementTypes_ReportsDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedRef, typeof({|#0:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedRef, typeof({|#1:MarshallerType<>|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementRef, typeof({|#2:MarshallerType<>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;
                
                    public static ReadOnlySpan<int> GetManagedValuesSource(ManagedType m) => default;
                
                    public static Span<T> GetUnmanagedValuesDestination(nint unmanaged, int numElements) => default;

                    public static ManagedType AllocateContainerForManagedElements(nint unmanaged, out int numElements) => throw null;
                
                    public static ReadOnlySpan<T> GetUnmanagedValuesSource(nint unmanaged, int numElements) => default;
                
                    public static Span<byte> GetManagedValuesDestination(ManagedType m) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(source,
                VerifyCS.Diagnostic(ElementTypesOfReturnTypesMustMatchRule).WithLocation(0).WithArguments("MarshallerType<T>.GetManagedValuesSource(ManagedType)", "MarshallerType<T>.GetManagedValuesDestination(ManagedType)"),
                VerifyCS.Diagnostic(ElementTypesOfReturnTypesMustMatchRule).WithLocation(1).WithArguments("MarshallerType<T>.GetManagedValuesSource(ManagedType)", "MarshallerType<T>.GetManagedValuesDestination(ManagedType)"),
                VerifyCS.Diagnostic(ElementTypesOfReturnTypesMustMatchRule).WithLocation(2).WithArguments("MarshallerType<T>.GetManagedValuesSource(ManagedType)", "MarshallerType<T>.GetManagedValuesDestination(ManagedType)"));
        }

        [Fact]
        public async Task ModeThatUsesBidirectionalShape_ArrayTarget_DoesNotReportDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                [CustomMarshaller(typeof(CustomMarshallerAttribute.GenericPlaceholder[]), MarshalMode.ManagedToUnmanagedRef, typeof({|#0:MarshallerType<,>|}))]
                [CustomMarshaller(typeof(CustomMarshallerAttribute.GenericPlaceholder[]), MarshalMode.UnmanagedToManagedRef, typeof({|#1:MarshallerType<,>|}))]
                [CustomMarshaller(typeof(CustomMarshallerAttribute.GenericPlaceholder[]), MarshalMode.ElementRef, typeof({|#2:MarshallerType<,>|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T, TNative>
                {
                    public static nint AllocateContainerForUnmanagedElements(T[] m, out int numElements) => throw null;
                
                    public static ReadOnlySpan<int> GetManagedValuesSource(T[] m) => default;
                
                    public static Span<TNative> GetUnmanagedValuesDestination(nint unmanaged, int numElements) => default;

                    public static T[] AllocateContainerForManagedElements(nint unmanaged, out int numElements) => throw null;
                
                    public static ReadOnlySpan<TNative> GetUnmanagedValuesSource(nint unmanaged, int numElements) => default;
                
                    public static Span<int> GetManagedValuesDestination(T[] m) => default;
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(source);
        }

        [Fact]
        public async Task ModeThatUsesBidirectionalShape_NestedGeneric_DoesNotReportDiagnostic()
        {
            string source = """
                using System;
                using System.Runtime.InteropServices.Marshalling;
                
                class ManagedType {}
                
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ManagedToUnmanagedRef, typeof({|#0:MarshallerType<>.Nested|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.UnmanagedToManagedRef, typeof({|#1:MarshallerType<>.Nested|}))]
                [CustomMarshaller(typeof(ManagedType), MarshalMode.ElementRef, typeof({|#2:MarshallerType<>.Nested|}))]
                [ContiguousCollectionMarshaller]
                static class MarshallerType<T>
                {
                    public static class Nested
                    {
                        public static nint AllocateContainerForUnmanagedElements(ManagedType m, out int numElements) => throw null;
                    
                        public static ReadOnlySpan<int> GetManagedValuesSource(ManagedType m) => default;
                    
                        public static Span<T> GetUnmanagedValuesDestination(nint unmanaged, int numElements) => default;

                        public static ManagedType AllocateContainerForManagedElements(nint unmanaged, out int numElements) => throw null;
                    
                        public static ReadOnlySpan<T> GetUnmanagedValuesSource(nint unmanaged, int numElements) => default;
                    
                        public static Span<int> GetManagedValuesDestination(ManagedType m) => default;
                    }
                }
                """;

            await VerifyCS.VerifyAnalyzerAsync(source);
        }
    }
}
