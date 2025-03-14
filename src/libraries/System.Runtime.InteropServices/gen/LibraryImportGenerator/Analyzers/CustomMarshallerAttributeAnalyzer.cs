// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.DotnetRuntime.Extensions;
using static Microsoft.Interop.Analyzers.AnalyzerDiagnostics;

namespace Microsoft.Interop.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CustomMarshallerAttributeAnalyzer : DiagnosticAnalyzer
    {
        private const string Category = "Usage";

        public static class MissingMemberNames
        {
            public const string Key = nameof(MissingMemberNames);
            public const char Delimiter = ' ';

            public const string MarshalModeKey = nameof(MarshalMode);

            public static ImmutableDictionary<string, string> CreateDiagnosticPropertiesForMissingMembersDiagnostic(MarshalMode mode, params string[] missingMemberNames)
                => CreateDiagnosticPropertiesForMissingMembersDiagnostic(mode, (IEnumerable<string>)missingMemberNames);

            public static ImmutableDictionary<string, string> CreateDiagnosticPropertiesForMissingMembersDiagnostic(MarshalMode mode, IEnumerable<string> missingMemberNames)
            {
                var builder = ImmutableDictionary.CreateBuilder<string, string>();
                builder.Add(MarshalModeKey, mode.ToString());
                builder.Add(Key, string.Join(Delimiter.ToString(), missingMemberNames));
                return builder.ToImmutable();
            }
        }

        /// <inheritdoc cref="SR.MarshallerTypeMustSpecifyManagedTypeMessage" />
        public static readonly DiagnosticDescriptor MarshallerTypeMustSpecifyManagedTypeRule =
            new DiagnosticDescriptor(
                Ids.InvalidCustomMarshallerAttributeUsage,
                GetResourceString(nameof(SR.InvalidCustomMarshallerAttributeUsageTitle)),
                GetResourceString(nameof(SR.MarshallerTypeMustSpecifyManagedTypeMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.MarshallerTypeMustSpecifyManagedTypeDescription)));

        /// <inheritdoc cref="SR.MarshallerTypeMustBeStaticClassOrStructMessage" />
        public static readonly DiagnosticDescriptor MarshallerTypeMustBeStaticClassOrStructRule =
            new DiagnosticDescriptor(
                Ids.InvalidMarshallerType,
                GetResourceString(nameof(SR.InvalidMarshallerTypeTitle)),
                GetResourceString(nameof(SR.MarshallerTypeMustBeStaticClassOrStructMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.MarshallerTypeMustBeStaticClassOrStructDescription)));

        /// <inheritdoc cref="SR.TypeMustBeUnmanagedMessage" />
        public static readonly DiagnosticDescriptor UnmanagedTypeMustBeUnmanagedRule =
            new DiagnosticDescriptor(
                Ids.InvalidNativeType,
                GetResourceString(nameof(SR.InvalidMarshallerTypeTitle)),
                GetResourceString(nameof(SR.TypeMustBeUnmanagedMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.TypeMustBeUnmanagedDescription)));

        /// <inheritdoc cref="SR.GetPinnableReferenceReturnTypeBlittableMessage" />
        public static readonly DiagnosticDescriptor GetPinnableReferenceReturnTypeBlittableRule =
            new DiagnosticDescriptor(
                Ids.InvalidSignaturesInMarshallerShape,
                GetResourceString(nameof(SR.InvalidSignaturesInMarshallerShapeTitle)),
                GetResourceString(nameof(SR.GetPinnableReferenceReturnTypeBlittableMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.GetPinnableReferenceReturnTypeBlittableDescription)));

        /// <inheritdoc cref="SR.TypeMustHaveExplicitCastFromVoidPointerMessage" />
        public static readonly DiagnosticDescriptor TypeMustHaveExplicitCastFromVoidPointerRule =
            new DiagnosticDescriptor(
                Ids.InvalidNativeType,
                GetResourceString(nameof(SR.InvalidMarshallerTypeTitle)),
                GetResourceString(nameof(SR.TypeMustHaveExplicitCastFromVoidPointerMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.TypeMustHaveExplicitCastFromVoidPointerDescription)));

        /// <inheritdoc cref="SR.StatelessValueInRequiresConvertToUnmanagedMessage" />
        public static readonly DiagnosticDescriptor StatelessValueInRequiresConvertToUnmanagedRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.StatelessValueInRequiresConvertToUnmanagedMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.StatelessValueInRequiresConvertToUnmanagedDescription)));

        /// <inheritdoc cref="SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsMessage" />
        public static readonly DiagnosticDescriptor StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsDescription)));

        /// <inheritdoc cref="SR.OutRequiresToManagedMessage" />
        public static readonly DiagnosticDescriptor OutRequiresToManagedRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.OutRequiresToManagedMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.OutRequiresToManagedDescription)));

        /// <inheritdoc cref="SR.StatelessRequiresConvertToManagedMessage" />
        public static readonly DiagnosticDescriptor StatelessRequiresConvertToManagedRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.StatelessRequiresConvertToManagedMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.StatelessRequiresConvertToManagedDescription)));

        /// <inheritdoc cref="SR.LinearCollectionInRequiresCollectionMethodsMessage" />
        public static readonly DiagnosticDescriptor LinearCollectionInRequiresCollectionMethodsRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.LinearCollectionInRequiresCollectionMethodsMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.LinearCollectionInRequiresCollectionMethodsDescription)));

        /// <inheritdoc cref="SR.StatelessLinearCollectionInRequiresCollectionMethodsMessage" />
        public static readonly DiagnosticDescriptor StatelessLinearCollectionInRequiresCollectionMethodsRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.StatelessLinearCollectionInRequiresCollectionMethodsMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.StatelessLinearCollectionInRequiresCollectionMethodsDescription)));

        /// <inheritdoc cref="SR.LinearCollectionOutRequiresCollectionMethodsMessage" />
        public static readonly DiagnosticDescriptor LinearCollectionOutRequiresCollectionMethodsRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.LinearCollectionOutRequiresCollectionMethodsMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.LinearCollectionOutRequiresCollectionMethodsDescription)));

        /// <inheritdoc cref="SR.StatelessLinearCollectionOutRequiresCollectionMethodsMessage" />
        public static readonly DiagnosticDescriptor StatelessLinearCollectionOutRequiresCollectionMethodsRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.StatelessLinearCollectionOutRequiresCollectionMethodsMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.StatelessLinearCollectionOutRequiresCollectionMethodsDescription)));

        /// <inheritdoc cref="SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsMessage" />
        public static readonly DiagnosticDescriptor StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsDescription)));

        /// <inheritdoc cref="SR.CallerAllocConstructorMustHaveBufferSizeMessage" />
        public static readonly DiagnosticDescriptor CallerAllocConstructorMustHaveBufferSizeRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.CallerAllocConstructorMustHaveBufferSizeMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.CallerAllocConstructorMustHaveBufferSizeDescription)));

        /// <inheritdoc cref="SR.CallerAllocConstructorMustHaveBufferSizeMessage" />
        public static readonly DiagnosticDescriptor StatelessLinearCollectionCallerAllocConstructorMustHaveBufferSizeRule =
            new DiagnosticDescriptor(
                Ids.CustomMarshallerTypeMustHaveRequiredShape,
                GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                GetResourceString(nameof(SR.StatelessLinearCollectionCallerAllocConstructorMustHaveBufferSizeMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.StatelessLinearCollectionCallerAllocConstructorMustHaveBufferSizeDescription)));

        /// <inheritdoc cref="SR.MarshallerTypeMustBeClosedOrMatchArityMessage" />
        public static readonly DiagnosticDescriptor MarshallerTypeMustBeClosedOrMatchArityRule =
            new DiagnosticDescriptor(
                Ids.InvalidCustomMarshallerAttributeUsage,
                GetResourceString(nameof(SR.InvalidMarshallerTypeTitle)),
                GetResourceString(nameof(SR.MarshallerTypeMustBeClosedOrMatchArityMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.MarshallerTypeMustBeClosedOrMatchArityDescription)));

        /// <inheritdoc cref="SR.MarshallerTypeMustBeNonNullMessage" />
        public static readonly DiagnosticDescriptor MarshallerTypeMustBeNonNullRule =
            new DiagnosticDescriptor(
                Ids.InvalidCustomMarshallerAttributeUsage,
                GetResourceString(nameof(SR.InvalidMarshallerTypeTitle)),
                GetResourceString(nameof(SR.MarshallerTypeMustBeNonNullMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.MarshallerTypeMustBeNonNullDescription)));

        /// <inheritdoc cref="SR.FirstParameterMustMatchReturnTypeMessage" />
        public static readonly DiagnosticDescriptor FirstParameterMustMatchReturnTypeRule =
            new DiagnosticDescriptor(
                Ids.InvalidSignaturesInMarshallerShape,
                GetResourceString(nameof(SR.InvalidSignaturesInMarshallerShapeTitle)),
                GetResourceString(nameof(SR.FirstParameterMustMatchReturnTypeMessage)),
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.FirstParameterMustMatchReturnTypeDescription)));

        /// <inheritdoc cref="SR.ReturnTypesMustMatchMessage" />
        public static readonly DiagnosticDescriptor ReturnTypesMustMatchRule =
            new DiagnosticDescriptor(
                Ids.InvalidSignaturesInMarshallerShape,
                GetResourceString(nameof(SR.InvalidSignaturesInMarshallerShapeTitle)),
                GetResourceString(nameof(SR.ReturnTypesMustMatchMessage)),
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.ReturnTypesMustMatchDescription)));

        /// <inheritdoc cref="SR.FirstParametersMustMatchMessage" />
        public static readonly DiagnosticDescriptor FirstParametersMustMatchRule =
            new DiagnosticDescriptor(
                Ids.InvalidSignaturesInMarshallerShape,
                GetResourceString(nameof(SR.InvalidSignaturesInMarshallerShapeTitle)),
                GetResourceString(nameof(SR.FirstParametersMustMatchMessage)),
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.FirstParametersMustMatchDescription)));

        /// <inheritdoc cref="SR.ElementTypesOfReturnTypesMustMatchMessage" />
        public static readonly DiagnosticDescriptor ElementTypesOfReturnTypesMustMatchRule =
            new DiagnosticDescriptor(
                Ids.InvalidSignaturesInMarshallerShape,
                GetResourceString(nameof(SR.InvalidSignaturesInMarshallerShapeTitle)),
                GetResourceString(nameof(SR.ElementTypesOfReturnTypesMustMatchMessage)),
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.ElementTypesOfReturnTypesMustMatchDescription)));

        /// <inheritdoc cref="SR.ReturnTypeMustBeExpectedTypeMessage" />
        public static readonly DiagnosticDescriptor ReturnTypeMustBeExpectedTypeRule =
            new DiagnosticDescriptor(
                Ids.InvalidSignaturesInMarshallerShape,
                GetResourceString(nameof(SR.InvalidSignaturesInMarshallerShapeTitle)),
                GetResourceString(nameof(SR.ReturnTypeMustBeExpectedTypeMessage)),
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.ReturnTypeMustBeExpectedTypeDescription)));

        /// <inheritdoc cref="SR.ManagedTypeMustBeClosedOrMatchArityMessage" />
        public static readonly DiagnosticDescriptor ManagedTypeMustBeClosedOrMatchArityRule =
            new DiagnosticDescriptor(
                Ids.InvalidCustomMarshallerAttributeUsage,
                GetResourceString(nameof(SR.InvalidManagedTypeTitle)),
                GetResourceString(nameof(SR.ManagedTypeMustBeClosedOrMatchArityMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.ManagedTypeMustBeClosedOrMatchArityDescription)));

        /// <inheritdoc cref="SR.ManagedTypeMustBeNonNullMessage" />
        public static readonly DiagnosticDescriptor ManagedTypeMustBeNonNullRule =
            new DiagnosticDescriptor(
                Ids.InvalidCustomMarshallerAttributeUsage,
                GetResourceString(nameof(SR.InvalidManagedTypeTitle)),
                GetResourceString(nameof(SR.ManagedTypeMustBeNonNullMessage)),
                Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true,
                description: GetResourceString(nameof(SR.ManagedTypeMustBeNonNullDescription)));

        // We are intentionally using the same diagnostic IDs as the parent type.
        // These diagnostics are the same diagnostics, but with a different severity,
        // as the Default marshaller shape can have support for the managed-to-unmanaged shape
        // the unmanaged-to-managed shape, or both.
#pragma warning disable RS1019
        public static class DefaultMarshalModeDiagnostics
        {
            /// <inheritdoc cref="CustomMarshallerAttributeAnalyzer.StatelessValueInRequiresConvertToUnmanagedRule" />
            private static readonly DiagnosticDescriptor StatelessValueInRequiresConvertToUnmanagedRule =
                new DiagnosticDescriptor(
                    Ids.CustomMarshallerTypeMustHaveRequiredShape,
                    GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                    GetResourceString(nameof(SR.StatelessValueInRequiresConvertToUnmanagedMessage)),
                    Category,
                    DiagnosticSeverity.Info,
                    isEnabledByDefault: true,
                    description: GetResourceString(nameof(SR.StatelessValueInRequiresConvertToUnmanagedDescription)));

            /// <inheritdoc cref="CustomMarshallerAttributeAnalyzer.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule" />
            private static readonly DiagnosticDescriptor StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule =
                new DiagnosticDescriptor(
                    Ids.CustomMarshallerTypeMustHaveRequiredShape,
                    GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                    GetResourceString(nameof(SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsMessage)),
                    Category,
                    DiagnosticSeverity.Info,
                    isEnabledByDefault: true,
                    description: GetResourceString(nameof(SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsDescription)));

            /// <inheritdoc cref="CustomMarshallerAttributeAnalyzer.StatelessRequiresConvertToManagedRule" />
            private static readonly DiagnosticDescriptor StatelessRequiresConvertToManagedRule =
                new DiagnosticDescriptor(
                    Ids.CustomMarshallerTypeMustHaveRequiredShape,
                    GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                    GetResourceString(nameof(SR.StatelessRequiresConvertToManagedMessage)),
                    Category,
                    DiagnosticSeverity.Info,
                    isEnabledByDefault: true,
                    description: GetResourceString(nameof(SR.StatelessRequiresConvertToManagedDescription)));

            /// <inheritdoc cref="CustomMarshallerAttributeAnalyzer.StatelessLinearCollectionInRequiresCollectionMethodsRule" />
            private static readonly DiagnosticDescriptor StatelessLinearCollectionInRequiresCollectionMethodsRule =
                new DiagnosticDescriptor(
                    Ids.CustomMarshallerTypeMustHaveRequiredShape,
                    GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                    GetResourceString(nameof(SR.StatelessLinearCollectionInRequiresCollectionMethodsMessage)),
                    Category,
                    DiagnosticSeverity.Info,
                    isEnabledByDefault: true,
                    description: GetResourceString(nameof(SR.StatelessLinearCollectionInRequiresCollectionMethodsDescription)));

            /// <inheritdoc cref="CustomMarshallerAttributeAnalyzer.StatelessLinearCollectionOutRequiresCollectionMethodsMessage" />
            private static readonly DiagnosticDescriptor StatelessLinearCollectionOutRequiresCollectionMethodsRule =
                new DiagnosticDescriptor(
                    Ids.CustomMarshallerTypeMustHaveRequiredShape,
                    GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                    GetResourceString(nameof(SR.StatelessLinearCollectionOutRequiresCollectionMethodsMessage)),
                    Category,
                    DiagnosticSeverity.Info,
                    isEnabledByDefault: true,
                    description: GetResourceString(nameof(SR.StatelessLinearCollectionOutRequiresCollectionMethodsDescription)));

            /// <inheritdoc cref="CustomMarshallerAttributeAnalyzer.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsMessage" />
            private static readonly DiagnosticDescriptor StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule =
                new DiagnosticDescriptor(
                    Ids.CustomMarshallerTypeMustHaveRequiredShape,
                    GetResourceString(nameof(SR.CustomMarshallerTypeMustHaveRequiredShapeTitle)),
                    GetResourceString(nameof(SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsMessage)),
                    Category,
                    DiagnosticSeverity.Info,
                    isEnabledByDefault: true,
                    description: GetResourceString(nameof(SR.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsDescription)));

            internal static DiagnosticDescriptor GetDefaultMarshalModeDiagnostic(DiagnosticDescriptor errorDescriptor)
            {
                if (ReferenceEquals(errorDescriptor, CustomMarshallerAttributeAnalyzer.StatelessValueInRequiresConvertToUnmanagedRule))
                {
                    return StatelessValueInRequiresConvertToUnmanagedRule;
                }
                if (ReferenceEquals(errorDescriptor, CustomMarshallerAttributeAnalyzer.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule))
                {
                    return StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule;
                }
                if (ReferenceEquals(errorDescriptor, CustomMarshallerAttributeAnalyzer.StatelessRequiresConvertToManagedRule))
                {
                    return StatelessRequiresConvertToManagedRule;
                }
                if (ReferenceEquals(errorDescriptor, CustomMarshallerAttributeAnalyzer.StatelessLinearCollectionInRequiresCollectionMethodsRule))
                {
                    return StatelessLinearCollectionInRequiresCollectionMethodsRule;
                }
                if (ReferenceEquals(errorDescriptor, CustomMarshallerAttributeAnalyzer.StatelessLinearCollectionOutRequiresCollectionMethodsRule))
                {
                    return StatelessLinearCollectionOutRequiresCollectionMethodsRule;
                }
                if (ReferenceEquals(errorDescriptor, CustomMarshallerAttributeAnalyzer.StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule))
                {
                    return StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule;
                }
                return errorDescriptor;
            }
        }
#pragma warning restore

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                MarshallerTypeMustSpecifyManagedTypeRule,
                UnmanagedTypeMustBeUnmanagedRule,
                GetPinnableReferenceReturnTypeBlittableRule,
                TypeMustHaveExplicitCastFromVoidPointerRule,
                StatelessValueInRequiresConvertToUnmanagedRule,
                StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule,
                OutRequiresToManagedRule,
                StatelessRequiresConvertToManagedRule,
                LinearCollectionInRequiresCollectionMethodsRule,
                StatelessLinearCollectionInRequiresCollectionMethodsRule,
                LinearCollectionOutRequiresCollectionMethodsRule,
                StatelessLinearCollectionOutRequiresCollectionMethodsRule,
                StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule,
                CallerAllocConstructorMustHaveBufferSizeRule,
                MarshallerTypeMustBeClosedOrMatchArityRule,
                FirstParameterMustMatchReturnTypeRule,
                ReturnTypesMustMatchRule,
                FirstParametersMustMatchRule,
                ElementTypesOfReturnTypesMustMatchRule,
                ManagedTypeMustBeClosedOrMatchArityRule,
                ManagedTypeMustBeNonNullRule,
                DefaultMarshalModeDiagnostics.GetDefaultMarshalModeDiagnostic(StatelessValueInRequiresConvertToUnmanagedRule),
                DefaultMarshalModeDiagnostics.GetDefaultMarshalModeDiagnostic(StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule),
                DefaultMarshalModeDiagnostics.GetDefaultMarshalModeDiagnostic(StatelessRequiresConvertToManagedRule),
                DefaultMarshalModeDiagnostics.GetDefaultMarshalModeDiagnostic(StatelessLinearCollectionInRequiresCollectionMethodsRule),
                DefaultMarshalModeDiagnostics.GetDefaultMarshalModeDiagnostic(StatelessLinearCollectionOutRequiresCollectionMethodsRule),
                DefaultMarshalModeDiagnostics.GetDefaultMarshalModeDiagnostic(StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule));

        public override void Initialize(AnalysisContext context)
        {
            // Don't analyze generated code
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(PrepareForAnalysis);
        }

        private void PrepareForAnalysis(CompilationStartAnalysisContext context)
        {
            if (context.Compilation.GetBestTypeByMetadataName(TypeNames.CustomMarshallerAttribute) is not null)
            {
                var perCompilationAnalyzer = new PerCompilationAnalyzer(context.Compilation);

                // TODO: Change this from a SyntaxNode action to an operation attribute once attribute application is represented in the
                // IOperation tree by Roslyn.
                context.RegisterSyntaxNodeAction(perCompilationAnalyzer.AnalyzeAttribute, SyntaxKind.Attribute);
            }
        }

        private sealed partial class PerCompilationAnalyzer
        {
            private readonly Compilation _compilation;
            private readonly INamedTypeSymbol _spanOfT;
            private readonly INamedTypeSymbol _readOnlySpanOfT;

            public PerCompilationAnalyzer(Compilation compilation)
            {
                _compilation = compilation;
                _spanOfT = compilation.GetBestTypeByMetadataName(TypeNames.System_Span_Metadata);
                _readOnlySpanOfT = compilation.GetBestTypeByMetadataName(TypeNames.System_ReadOnlySpan_Metadata);
            }

            public void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
            {
                AttributeSyntax syntax = (AttributeSyntax)context.Node;
                ISymbol attributedSymbol = context.ContainingSymbol!;

                AttributeData? attr = syntax.FindAttributeData(attributedSymbol);
                if (attr?.AttributeClass?.ToDisplayString() == TypeNames.CustomMarshallerAttribute
                    && attr.AttributeConstructor is not null)
                {
                    DiagnosticReporter managedTypeReporter = DiagnosticReporter.CreateForLocation(syntax.FindArgumentWithNameOrArity("managedType", 0).FindTypeExpressionOrNullLocation(), context.ReportDiagnostic);
                    INamedTypeSymbol entryType = (INamedTypeSymbol)attributedSymbol;

                    ITypeSymbol? managedTypeInAttribute = (ITypeSymbol?)attr.ConstructorArguments[0].Value;
                    if (managedTypeInAttribute is null)
                    {
                        managedTypeReporter.CreateAndReportDiagnostic(ManagedTypeMustBeNonNullRule, entryType.ToDisplayString());
                        return;
                    }

                    if (!ManualTypeMarshallingHelper.TryResolveManagedType(
                        entryType,
                        ManualTypeMarshallingHelper.ReplaceGenericPlaceholderInType(managedTypeInAttribute, entryType, context.Compilation),
                        ManualTypeMarshallingHelper.IsLinearCollectionEntryPoint(entryType),
                        (entryType, managedType) => managedTypeReporter.CreateAndReportDiagnostic(ManagedTypeMustBeClosedOrMatchArityRule, managedType, entryType), out ITypeSymbol managedType))
                    {
                        return;
                    }
                    DiagnosticReporter marshallerTypeReporter = DiagnosticReporter.CreateForLocation(syntax.FindArgumentWithNameOrArity("marshallerType", 2).FindTypeExpressionOrNullLocation(), context.ReportDiagnostic);
                    ITypeSymbol? marshallerTypeInAttribute = (ITypeSymbol?)attr.ConstructorArguments[2].Value;
                    if (marshallerTypeInAttribute is null)
                    {
                        marshallerTypeReporter.CreateAndReportDiagnostic(MarshallerTypeMustBeNonNullRule);
                        return;
                    }
                    if (!ManualTypeMarshallingHelper.TryResolveMarshallerType(
                        entryType,
                        marshallerTypeInAttribute,
                        (entryType, marshallerType) => marshallerTypeReporter.CreateAndReportDiagnostic(MarshallerTypeMustBeClosedOrMatchArityRule, marshallerType, entryType),
                        out ITypeSymbol marshallerType))
                    {
                        return;
                    }

                    AnalyzeMarshallerType(
                        marshallerTypeReporter,
                        managedType,
                        (MarshalMode)attr.ConstructorArguments[1].Value,
                        (INamedTypeSymbol)marshallerType,
                        ManualTypeMarshallingHelper.IsLinearCollectionEntryPoint(entryType));
                }
            }

            private void AnalyzeMarshallerType(DiagnosticReporter diagnosticReporter, ITypeSymbol managedType, MarshalMode mode, INamedTypeSymbol marshallerType, bool isLinearCollectionMarshaller)
            {
                if (marshallerType.IsReferenceType && marshallerType.IsStatic)
                {
                    AnalyzeStatelessMarshallerType(diagnosticReporter, managedType, mode, marshallerType, isLinearCollectionMarshaller);
                }
                else if (marshallerType.IsValueType)
                {
                    AnalyzeStatefulMarshallerType(diagnosticReporter, managedType, mode, marshallerType, isLinearCollectionMarshaller);
                }
                else
                {
                    diagnosticReporter.CreateAndReportDiagnostic(MarshallerTypeMustBeStaticClassOrStructRule, marshallerType.ToDisplayString());
                }
            }

            private void AnalyzeStatelessMarshallerType(DiagnosticReporter diagnosticReporter, ITypeSymbol managedType, MarshalMode mode, INamedTypeSymbol marshallerType, bool isLinearCollectionMarshaller)
            {
                var (shape, methods) = StatelessMarshallerShapeHelper.GetShapeForType(marshallerType, managedType, isLinearCollectionMarshaller, _compilation);

                bool reportedDiagnostics = false;
                DiagnosticReporter trackingReporter = new DiagnosticReporter((descriptor, properties, args) =>
                {
                    reportedDiagnostics = true;
                    diagnosticReporter.CreateAndReportDiagnostic(descriptor, properties, args);
                });
                trackingReporter = AdaptReporterForMarshalMode(trackingReporter, mode);

                ReportDiagnosticsForMissingMembers(trackingReporter);

                // If we encountered any missing-member diagnostics, then we'll stop checking for additional errors here.
                if (reportedDiagnostics)
                    return;

                ReportDiagnosticsForMismatchedMemberSignatures(trackingReporter);

                void ReportDiagnosticsForMissingMembers(DiagnosticReporter diagnosticReporter)
                {
                    // If a caller-allocated-buffer convert method exists, verify that the BufferSize property exists
                    if (shape.HasFlag(MarshallerShape.CallerAllocatedBuffer) && mode == MarshalMode.ManagedToUnmanagedIn)
                    {
                        CheckForBufferSizeMember(
                            diagnosticReporter,
                            isLinearCollectionMarshaller ? StatelessLinearCollectionCallerAllocConstructorMustHaveBufferSizeRule : CallerAllocConstructorMustHaveBufferSizeRule,
                            marshallerType,
                            methods.ToUnmanagedWithBuffer!);
                    }

                    if (ManualTypeMarshallingHelper.ModeUsesManagedToUnmanagedShape(mode))
                    {
                        // If the marshaller mode uses the managed->unmanaged shapes,
                        // verify that we have either a full managed-to-unmanaged shape
                        // or that our scenario supports the caller-allocated buffer managed-to-unmanaged shape
                        // and that the caller-allocated-buffer shape is present.
                        if (!(shape.HasFlag(MarshallerShape.ToUnmanaged) || (mode == MarshalMode.ManagedToUnmanagedIn && shape.HasFlag(MarshallerShape.CallerAllocatedBuffer))))
                        {
                            if (isLinearCollectionMarshaller)
                            {
                                // Verify that all of the following methods are present with valid shapes:
                                // - AllocateContainerForUnmanagedElements
                                // - GetManagedValuesSource
                                // - GetUnmanagedValuesDestination
                                if (methods.ToUnmanaged is null && methods.ToUnmanagedWithBuffer is null)
                                {
                                    diagnosticReporter.CreateAndReportDiagnostic(
                                        StatelessLinearCollectionRequiresTwoParameterAllocateContainerForUnmanagedElementsRule,
                                        MissingMemberNames.CreateDiagnosticPropertiesForMissingMembersDiagnostic(
                                            mode,
                                            ShapeMemberNames.LinearCollection.Stateless.AllocateContainerForUnmanagedElements),
                                        marshallerType.ToDisplayString(),
                                        mode,
                                        managedType.ToDisplayString());
                                }
                                List<string> missingCollectionMethods = new();
                                if (methods.ManagedValuesSource is null)
                                {
                                    missingCollectionMethods.Add(ShapeMemberNames.LinearCollection.Stateless.GetManagedValuesSource);
                                }
                                if (methods.UnmanagedValuesDestination is null)
                                {
                                    missingCollectionMethods.Add(ShapeMemberNames.LinearCollection.Stateless.GetUnmanagedValuesDestination);
                                }
                                if (missingCollectionMethods.Count > 0)
                                {
                                    diagnosticReporter.CreateAndReportDiagnostic(
                                        StatelessLinearCollectionInRequiresCollectionMethodsRule,
                                        MissingMemberNames.CreateDiagnosticPropertiesForMissingMembersDiagnostic(
                                            mode,
                                            missingCollectionMethods),
                                        marshallerType.ToDisplayString(),
                                        mode,
                                        managedType.ToDisplayString());
                                }
                            }
                            else
                            {
                                // Verify that all of the following methods are present with valid shapes:
                                // - ConvertToUnmanaged
                                diagnosticReporter.CreateAndReportDiagnostic(
                                    StatelessValueInRequiresConvertToUnmanagedRule,
                                    MissingMemberNames.CreateDiagnosticPropertiesForMissingMembersDiagnostic(mode, ShapeMemberNames.Value.Stateless.ConvertToUnmanaged),
                                        marshallerType.ToDisplayString(),
                                        mode,
                                        managedType.ToDisplayString());
                            }
                        }
                    }

                    if (ManualTypeMarshallingHelper.ModeUsesUnmanagedToManagedShape(mode))
                    {
                        // If the marshaller mode uses the unmanaged->managed shapes,
                        // verify that we have a full unmanaged-to-managed shape
                        if ((shape & (MarshallerShape.ToManaged | MarshallerShape.GuaranteedUnmarshal)) == 0)
                        {
                            if (isLinearCollectionMarshaller)
                            {
                                // Verify that all of the following methods are present with valid shapes:
                                // - AllocateContainerForUnmanagedElements
                                // - GetUnmanagedValuesSource
                                // - GetManagedValuesDestination
                                if (methods.ToManaged is null && methods.ToManagedFinally is null)
                                {
                                    diagnosticReporter.CreateAndReportDiagnostic(
                                        StatelessLinearCollectionRequiresTwoParameterAllocateContainerForManagedElementsRule,
                                        MissingMemberNames.CreateDiagnosticPropertiesForMissingMembersDiagnostic(
                                            mode,
                                            ShapeMemberNames.LinearCollection.Stateless.AllocateContainerForUnmanagedElements),
                                        marshallerType.ToDisplayString(),
                                        mode,
                                        managedType.ToDisplayString());
                                }
                                List<string> missingCollectionMethods = new();
                                if (methods.UnmanagedValuesSource is null)
                                {
                                    missingCollectionMethods.Add(ShapeMemberNames.LinearCollection.Stateless.GetUnmanagedValuesSource);
                                }
                                if (methods.ManagedValuesDestination is null)
                                {
                                    missingCollectionMethods.Add(ShapeMemberNames.LinearCollection.Stateless.GetManagedValuesDestination);
                                }
                                if (missingCollectionMethods.Count > 0)
                                {
                                    diagnosticReporter.CreateAndReportDiagnostic(
                                        StatelessLinearCollectionOutRequiresCollectionMethodsRule,
                                        MissingMemberNames.CreateDiagnosticPropertiesForMissingMembersDiagnostic(
                                            mode,
                                            missingCollectionMethods),
                                        marshallerType.ToDisplayString(),
                                        mode,
                                        managedType.ToDisplayString());
                                }
                            }
                            else
                            {
                                // Verify that all of the following methods are present with valid shapes:
                                // - ConvertToManaged
                                diagnosticReporter.CreateAndReportDiagnostic(StatelessRequiresConvertToManagedRule,
                                    MissingMemberNames.CreateDiagnosticPropertiesForMissingMembersDiagnostic(
                                        mode,
                                        ShapeMemberNames.Value.Stateless.ConvertToManaged),
                                    marshallerType.ToDisplayString(),
                                    mode,
                                    managedType.ToDisplayString());
                            }
                        }
                    }
                }

                void ReportDiagnosticsForMismatchedMemberSignatures(DiagnosticReporter diagnosticReporter)
                {
                    // Verify that the unmanaged type used by the marshaller is consistently
                    // the same in all of the methods that use the unmanaged type.
                    // Also, verify that the collection element types are consistent.
                    ITypeSymbol? unmanagedType = null;
                    if (ManualTypeMarshallingHelper.ModeUsesManagedToUnmanagedShape(mode))
                    {
                        // First verify all usages in the managed->unmanaged shape.
                        IMethodSymbol toUnmanagedMethod = methods.ToUnmanaged ?? methods.ToUnmanagedWithBuffer;
                        unmanagedType = toUnmanagedMethod.ReturnType;
                        if (!unmanagedType.IsUnmanagedType && !unmanagedType.IsStrictlyBlittable())
                        {
                            diagnosticReporter.CreateAndReportDiagnostic(UnmanagedTypeMustBeUnmanagedRule, toUnmanagedMethod.ToDisplayString());
                        }

                        if (isLinearCollectionMarshaller)
                        {
                            if (!SymbolEqualityComparer.Default.Equals(methods.UnmanagedValuesDestination.Parameters[0].Type, unmanagedType))
                            {
                                diagnosticReporter.CreateAndReportDiagnostic(FirstParameterMustMatchReturnTypeRule, methods.UnmanagedValuesDestination.ToDisplayString(), toUnmanagedMethod.ToDisplayString());
                            }
                        }

                        if (shape.HasFlag(MarshallerShape.ToUnmanaged | MarshallerShape.CallerAllocatedBuffer))
                        {
                            // If the marshaller has both "ConvertToUnmanaged" method variants, verify that their return types match.
                            if (!SymbolEqualityComparer.Default.Equals(methods.ToUnmanaged.ReturnType, methods.ToUnmanagedWithBuffer.ReturnType))
                            {
                                diagnosticReporter.CreateAndReportDiagnostic(ReturnTypesMustMatchRule, methods.ToUnmanaged.ToDisplayString(), methods.ToUnmanagedWithBuffer.ToDisplayString());
                            }
                        }
                    }

                    if (ManualTypeMarshallingHelper.ModeUsesUnmanagedToManagedShape(mode))
                    {
                        // Verify the usages unmanaged->managed shape
                        IMethodSymbol toManagedMethod = methods.ToManaged ?? methods.ToManagedFinally;

                        if (unmanagedType is not null && !SymbolEqualityComparer.Default.Equals(unmanagedType, toManagedMethod.Parameters[0].Type))
                        {
                            // If both shapes are present, verify that the unmanaged types match
                            diagnosticReporter.CreateAndReportDiagnostic(FirstParameterMustMatchReturnTypeRule, toManagedMethod.ToDisplayString(), (methods.ToUnmanaged ?? methods.ToUnmanagedWithBuffer).ToDisplayString());
                        }

                        unmanagedType = toManagedMethod.Parameters[0].Type;

                        if (isLinearCollectionMarshaller)
                        {
                            if (!SymbolEqualityComparer.Default.Equals(methods.UnmanagedValuesSource.Parameters[0].Type, unmanagedType))
                            {
                                diagnosticReporter.CreateAndReportDiagnostic(FirstParametersMustMatchRule, methods.UnmanagedValuesSource.ToDisplayString(), toManagedMethod.ToDisplayString());
                            }
                        }

                        if (shape.HasFlag(MarshallerShape.ToManaged | MarshallerShape.GuaranteedUnmarshal))
                        {
                            // If the marshaller has both "ConvertToUnmanaged" method variants, verify that their parameter types match.
                            if (!SymbolEqualityComparer.Default.Equals(methods.ToManaged.Parameters[1].Type, methods.ToManagedFinally.Parameters[1].Type))
                            {
                                diagnosticReporter.CreateAndReportDiagnostic(FirstParametersMustMatchRule, methods.ToManaged.ToDisplayString(), methods.ToManagedFinally.ToDisplayString());
                            }
                        }
                    }

                    // Verify that the managed collection element types match.
                    // Verify that the unmanaged collection types have the expected element types.
                    if (isLinearCollectionMarshaller)
                    {
                        if (methods.ManagedValuesSource is not null && methods.ManagedValuesDestination is not null)
                        {
                            if (TryGetElementTypeFromSpanType(methods.ManagedValuesSource.ReturnType, out ITypeSymbol sourceElementType)
                                && TryGetElementTypeFromSpanType(methods.ManagedValuesDestination.ReturnType, out ITypeSymbol destinationElementType)
                                && !SymbolEqualityComparer.Default.Equals(sourceElementType, destinationElementType))
                            {
                                diagnosticReporter.CreateAndReportDiagnostic(ElementTypesOfReturnTypesMustMatchRule, methods.ManagedValuesSource.ToDisplayString(), methods.ManagedValuesDestination.ToDisplayString());
                            }
                        }

                        var (typeArguments, _) = marshallerType.GetAllTypeArgumentsIncludingInContainingTypes();
                        ITypeSymbol expectedUnmanagedCollectionElementType = typeArguments[typeArguments.Length - 1];
                        VerifyUnmanagedCollectionElementType(diagnosticReporter, methods.UnmanagedValuesSource, expectedUnmanagedCollectionElementType, _readOnlySpanOfT);
                        VerifyUnmanagedCollectionElementType(diagnosticReporter, methods.UnmanagedValuesDestination, expectedUnmanagedCollectionElementType, _spanOfT);
                    }
                }
            }

            private void VerifyUnmanagedCollectionElementType(DiagnosticReporter diagnosticReporter, IMethodSymbol? unmanagedValuesCollectionMethod, ITypeSymbol expectedElementType, INamedTypeSymbol expectedSpanType)
            {
                if (unmanagedValuesCollectionMethod is not null
                    && TryGetElementTypeFromSpanType(unmanagedValuesCollectionMethod.ReturnType, out ITypeSymbol sourceElementType)
                    && !SymbolEqualityComparer.Default.Equals(sourceElementType, expectedElementType))
                {
                    diagnosticReporter.CreateAndReportDiagnostic(ReturnTypeMustBeExpectedTypeRule, unmanagedValuesCollectionMethod.ToDisplayString(), expectedSpanType.Construct(expectedElementType).ToDisplayString());
                }
            }

            private static DiagnosticReporter AdaptReporterForMarshalMode(DiagnosticReporter trackingReporter, MarshalMode mode)
            {
                if (mode == MarshalMode.Default)
                {
                    return new DiagnosticReporter((descriptor, properties, args) => trackingReporter.CreateAndReportDiagnostic(DefaultMarshalModeDiagnostics.GetDefaultMarshalModeDiagnostic(descriptor), properties, args));
                }

                return trackingReporter;
            }

            private static void CheckForBufferSizeMember(DiagnosticReporter diagnosticReporter, DiagnosticDescriptor descriptor, INamedTypeSymbol marshallerType, IMethodSymbol callerAllocatedBufferMethod)
            {
                if (marshallerType.GetMembers(ShapeMemberNames.BufferSize).OfType<IPropertySymbol>().FirstOrDefault(prop => prop is { ReturnsByRef: false, ReturnsByRefReadonly: false, GetMethod: not null }) is null)
                {
                    INamedTypeSymbol allocatedBufferType = (INamedTypeSymbol)callerAllocatedBufferMethod.Parameters[1].Type;
                    diagnosticReporter.CreateAndReportDiagnostic(
                        descriptor,
                        MissingMemberNames.CreateDiagnosticPropertiesForMissingMembersDiagnostic(MarshalMode.ManagedToUnmanagedIn, ShapeMemberNames.BufferSize),
                        marshallerType.ToDisplayString(),
                        allocatedBufferType.TypeArguments[0].ToDisplayString());
                }
            }

            private bool TryGetElementTypeFromSpanType(ITypeSymbol spanTypeMaybe, [NotNullWhen(true)] out ITypeSymbol? elementType)
            {
                if (SymbolEqualityComparer.Default.Equals(spanTypeMaybe.OriginalDefinition, _spanOfT) || SymbolEqualityComparer.Default.Equals(spanTypeMaybe.OriginalDefinition, _readOnlySpanOfT))
                {
                    elementType = ((INamedTypeSymbol)spanTypeMaybe).TypeArguments[0];
                    return true;
                }
                elementType = null;
                return false;
            }

#pragma warning disable CA1822 // Mark members as static
            private void AnalyzeStatefulMarshallerType(DiagnosticReporter diagnosticReporter, ITypeSymbol managedType, MarshalMode mode, INamedTypeSymbol marshallerType, bool isLinearCollectionMarshaller)
#pragma warning restore CA1822 // Mark members as static
            {
            }
        }
    }
}
