// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ShaderTools.CodeAnalysis.Compilation;
using ShaderTools.CodeAnalysis.Symbols;
using ShaderTools.Utilities.Collections;
using TaggedText = Microsoft.CodeAnalysis.TaggedText;

namespace ShaderTools.CodeAnalysis.Shared.Extensions
{
    internal static partial class ISymbolExtensions2
    {
        public static Glyph GetGlyph(this ISymbol symbol)
        {
            return symbol.Kind switch
            {
                SymbolKind.Array => Glyph.Class,
                SymbolKind.Namespace => Glyph.Namespace,
                SymbolKind.Struct => Glyph.Structure,
                SymbolKind.Class => Glyph.Class,
                SymbolKind.Interface => Glyph.Interface,
                SymbolKind.Field => Glyph.Field,
                SymbolKind.Function => Glyph.Method,
                SymbolKind.Variable => Glyph.Local, // Not quite right.
                SymbolKind.Parameter => Glyph.Parameter,
                SymbolKind.Indexer => Glyph.Method,
                SymbolKind.IntrinsicObjectType => Glyph.IntrinsicClass,
                SymbolKind.IntrinsicVectorType => Glyph.IntrinsicStruct,
                SymbolKind.IntrinsicMatrixType => Glyph.IntrinsicStruct,
                SymbolKind.IntrinsicScalarType => Glyph.IntrinsicStruct,
                SymbolKind.Semantic => Glyph.Constant,
                SymbolKind.Technique or SymbolKind.MessiahTechnique => Glyph.Module,
                SymbolKind.Attribute => Glyph.Method,
                SymbolKind.ConstantBuffer => Glyph.Structure,
                SymbolKind.TypeAlias => Glyph.Typedef,
                SymbolKind.Toggle => Glyph.Toggle,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public static string GetFullyQualifiedName(this ISymbol symbol)
        {
            var result = new StringBuilder();
            GetFullyQualifiedNameRecursive(symbol, result);
            return result.ToString();
        }

        private static void GetFullyQualifiedNameRecursive(this ISymbol symbol, StringBuilder sb)
        {
            if (symbol.Parent != null)
            {
                GetFullyQualifiedNameRecursive(symbol.Parent, sb);
                sb.Append("::");
            }

            sb.Append(symbol.Name);
        }

        private static string GetDocumentation(ISymbol symbol, CancellationToken cancellationToken)
        {
            return symbol.Documentation;
        }

        public static IEnumerable<TaggedText> GetDocumentationParts(this ISymbol symbol, SemanticModelBase semanticModel, int position, CancellationToken cancellationToken)
        {
            string documentation = GetDocumentation(symbol, cancellationToken);

            return documentation != null
                ? SpecializedCollections.SingletonEnumerable(new TaggedText(TextTags.Text, documentation))
                : SpecializedCollections.EmptyEnumerable<TaggedText>();
        }

        public static string ToDisplayString(this ISymbol symbol, SymbolDisplayFormat format)
        {
            return symbol.ToMarkup(format).Tokens.ToTaggedText().GetFullText();
        }
    }
}