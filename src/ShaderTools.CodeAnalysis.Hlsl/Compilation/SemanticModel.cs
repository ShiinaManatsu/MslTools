using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ABI.Windows.ApplicationModel.Background;
using ShaderTools.CodeAnalysis.Compilation;
using ShaderTools.CodeAnalysis.Diagnostics;
using ShaderTools.CodeAnalysis.Hlsl.Binding;
using ShaderTools.CodeAnalysis.Hlsl.Binding.BoundNodes;
using ShaderTools.CodeAnalysis.Hlsl.Symbols;
using ShaderTools.CodeAnalysis.Hlsl.Syntax;
using ShaderTools.CodeAnalysis.Symbols;
using ShaderTools.CodeAnalysis.Syntax;
using ShaderTools.CodeAnalysis.Text;
using System.Collections.Immutable;
using Binder = ShaderTools.CodeAnalysis.Hlsl.Binding.Binder;

namespace ShaderTools.CodeAnalysis.Hlsl.Compilation
{
    public sealed class SemanticModel : SemanticModelBase
    {
        private readonly BindingResult _bindingResult;

        public Compilation Compilation { get; }

        public override SyntaxTreeBase SyntaxTree => Compilation.SyntaxTree;

        public override string Language => SyntaxTree.Root.Language;

        internal SemanticModel(Compilation compilation, BindingResult bindingResult)
        {
            Compilation = compilation;
            _bindingResult = bindingResult;
        }

        public SyntaxNode BindingRoot => _bindingResult.Root;

        private List<(string Label, bool IsParameter, SourceRange SourceRange)> MappingFunctionInvocation(
            BoundFunctionInvocationExpression x, SyntaxNode syntaxNode, bool withType)
        {
            var result = new List<(string Label, bool IsParameter, SourceRange SourceRange)>();
            // result.Add((x.Type.Name, false,syntaxNode.SourceRange));

            if (x.Arguments.IsEmpty) return result;

            if (syntaxNode is not FunctionInvocationExpressionSyntax syntax) return result;
            if (x.Symbol == null) return result;
            result.AddRange(x.Symbol.Parameters
                .Take(syntax.ArgumentList.Arguments.Count)
                .Select((p, i) => ($"{p.Name}:{(withType ? p.ValueType.Name : string.Empty)}", true,
                    syntax.ArgumentList.Arguments[i].SourceRange)));
            return result;
        }

        public List<(string Label, bool IsParameter, SourceRange SourceRange)> GetBoundNode(SyntaxNode syntaxNode,
            bool withType)
        {
            var bn = _bindingResult.GetBoundNode(syntaxNode);
            if (bn == null) return [];

            return bn switch
            {
                BoundVariableExpression x => [(x.Type.Name, true, syntaxNode.SourceRange)],
                BoundFunctionInvocationExpression x => MappingFunctionInvocation(x, syntaxNode, withType),
                BoundFieldExpression x => [(x.Type.Name, true, syntaxNode.SourceRange)],
                _ => []
            };
        }

        public IEnumerable<VariableDeclaratorSyntax> GetLocalTextures()
        {
            var textures = ((BoundCompilationUnit)_bindingResult.BoundRoot).Declarations
                .OfType<BoundMultipleVariableDeclarations>()
                .Select(x => x.VariableDeclarations.First())
                .Where(x =>
                {
                    if (x.DeclaredType is IntrinsicObjectTypeSymbol symbol)
                    {
                        return Is2DVariant(symbol.PredefinedType);
                    }

                    return false;
                })
                .Select(x => x.VariableSymbol)
                .OfType<SourceVariableSymbol>()
                .SelectMany(x => x.DeclaringSyntaxNodes)
                .OfType<VariableDeclaratorSyntax>()
                .ToList();

            return textures;

            static bool Is2DVariant(PredefinedObjectType type)
            {
                return type switch
                {
                    PredefinedObjectType.Texture or
                        PredefinedObjectType.Texture1D or
                        PredefinedObjectType.Texture2D or
                        PredefinedObjectType.Texture2DMS or
                        PredefinedObjectType.Texture3D or
                        PredefinedObjectType.TextureCube => true,
                    _ => false
                };
            }
        }

        public IEnumerable<(string, string)> GetLocalUserTextures()
        {
            return ((BoundCompilationUnit)_bindingResult.BoundRoot).Declarations
                .OfType<BoundMultipleVariableDeclarations>()
                .SelectMany(x => x.VariableDeclarations)
                .Where(x => x.DeclaredType.Name != "[Unknown]" && x.DeclaredType is IntrinsicObjectTypeSymbol
                {
                    PredefinedType: PredefinedObjectType.Texture2D or
                    PredefinedObjectType.Texture3D
                })
                .Where(x => !x.Qualifiers.Any(q => q is BoundSemantic))
                .Select(x => (x.DeclaredType.Name, x.VariableSymbol.Name));
        }

        public IEnumerable<(string, string)> GetConstantBufferByName(string name, bool filterInvisible = true)
        {
            return ((BoundCompilationUnit)_bindingResult.BoundRoot).Declarations
                .OfType<BoundConstantBuffer>()
                .Where(x => x.ConstantBufferSymbol.Name == name)
                .SelectMany(x => x.Variables)
                .SelectMany(x => x.VariableDeclarations)
                .Where(FilterAnnotation)
                .Select(x => (x.DeclaredType.Name, x.VariableSymbol.Name));

            bool FilterAnnotation(BoundVariableDeclaration boundVariableDeclaration)
            {
                var keep = true;
                foreach (var declaringSyntaxNode in boundVariableDeclaration.VariableSymbol.DeclaringSyntaxNodes)
                {
                    if (declaringSyntaxNode is not VariableDeclaratorSyntax variableDeclaratorSyntax ||
                        variableDeclaratorSyntax.Annotations == null) continue;
                    foreach (var variable in from annotation in variableDeclaratorSyntax.Annotations.Annotations
                             from variable in annotation.Declaration.Variables
                             where filterInvisible
                             select variable)
                    {
                        if (variable.Identifier.IsFirstTokenInMacroExpansion)
                        {
                            if (variable.Identifier.MacroReference.DefineDirective.ToString()
                                .Contains("visible = \"false\"", StringComparison.OrdinalIgnoreCase))
                            {
                                keep = false;
                            }
                        }
                        else
                        {
                            if (variable.Initializer is not EqualsValueClauseSyntax equalsValueClause) continue;
                            var isVisible = variable.Identifier.ToString()
                                .Contains("visible", StringComparison.OrdinalIgnoreCase);
                            var isFalse = equalsValueClause.Value.ToString()
                                .Contains("false", StringComparison.OrdinalIgnoreCase);
                            if (isVisible && isFalse)
                            {
                                keep = false;
                            }
                        }
                    }
                }

                return keep;
            }
        }

        public IEnumerable<(string, string)> GetLocalToggles()
        {
            return ((BoundCompilationUnit)_bindingResult.BoundRoot).Declarations
                .OfType<BoundToggle>()
                .Select(x => x.ToggleSymbol)
                .Where(x => !x.Syntax.StateInitializer.Properties.Any(property =>
                    (property.Name.ToString() == "Visible" && property.Value.ToString() == "false") ||
                    property.Name.ToString() == "AffectedTex"))
                .Select(x => ("Toggle", x.Name));
        }

        public override ISymbol GetDeclaredSymbol(SyntaxNodeBase declaration)
        {
            var node = (SyntaxNode)declaration;

            var parameter = node as ParameterSyntax;
            if (parameter != null)
                return GetDeclaredSymbol(parameter);

            var @namespace = node as NamespaceSyntax;
            if (@namespace != null)
                return GetDeclaredSymbol(@namespace);

            var interfaceType = node as InterfaceTypeSyntax;
            if (interfaceType != null)
                return GetDeclaredSymbol(interfaceType);

            var structType = node as StructTypeSyntax;
            if (structType != null)
                return GetDeclaredSymbol(structType);

            var variableDeclarator = node as VariableDeclaratorSyntax;
            if (variableDeclarator != null)
                return GetDeclaredSymbol(variableDeclarator);

            var typeAlias = node as TypeAliasSyntax;
            if (typeAlias != null)
                return GetDeclaredSymbol(typeAlias);

            var constantBuffer = node as ConstantBufferSyntax;
            if (constantBuffer != null)
                return GetDeclaredSymbol(constantBuffer);

            var functionDeclaration = node as FunctionDeclarationSyntax;
            if (functionDeclaration != null)
                return GetDeclaredSymbol(functionDeclaration);

            var functionDefinition = node as FunctionDefinitionSyntax;
            if (functionDefinition != null)
                return GetDeclaredSymbol(functionDefinition);

            var technique = node as TechniqueSyntax;
            if (technique != null)
                return GetDeclaredSymbol(technique);

            var messiahTechnique = node as MessiahTechniqueSyntax;
            if (messiahTechnique != null)
                return GetDeclaredSymbol(messiahTechnique);

            var toggle = node as ToggleDefinitionSyntax;
            if (toggle != null)
                return GetDeclaredSymbol(toggle);

            return null;
        }

        public ToggleSymbol GetDeclaredSymbol(ToggleDefinitionSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundToggle;
            return result?.ToggleSymbol;
        }

        public ParameterSymbol GetDeclaredSymbol(ParameterSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax.Declarator) as BoundVariableDeclaration;
            return result?.VariableSymbol as ParameterSymbol;
        }

        public NamespaceSymbol GetDeclaredSymbol(NamespaceSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundNamespace;
            return result?.NamespaceSymbol;
        }

        public InterfaceSymbol GetDeclaredSymbol(InterfaceTypeSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundInterfaceType;
            return result?.InterfaceSymbol;
        }

        public StructSymbol GetDeclaredSymbol(StructTypeSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundStructType;
            return result?.StructSymbol;
        }

        public VariableSymbol GetDeclaredSymbol(VariableDeclaratorSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundVariableDeclaration;
            return result?.VariableSymbol;
        }

        public TypeAliasSymbol GetDeclaredSymbol(TypeAliasSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundTypeAlias;
            return result?.TypeAliasSymbol;
        }

        public ConstantBufferSymbol GetDeclaredSymbol(ConstantBufferSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundConstantBuffer;
            return result?.ConstantBufferSymbol;
        }

        public FunctionSymbol GetDeclaredSymbol(FunctionDeclarationSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundFunction;
            return result?.FunctionSymbol;
        }

        public FunctionSymbol GetDeclaredSymbol(FunctionDefinitionSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundFunction;
            return result?.FunctionSymbol;
        }

        public TechniqueSymbol GetDeclaredSymbol(TechniqueSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundTechnique;
            return result?.TechniqueSymbol;
        }

        public MessiahTechniqueSymbol GetDeclaredSymbol(MessiahTechniqueSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundMessiahTechnique;
            return result?.TechniqueSymbol;
        }

        public override SymbolInfo GetSymbolInfo(SyntaxNodeBase node)
        {
            Symbol getSymbol()
            {
                var identifierDeclarationName = node as IdentifierDeclarationNameSyntax;
                if (identifierDeclarationName != null)
                    return GetSymbol(identifierDeclarationName);

                var semantic = node as SemanticSyntax;
                if (semantic != null)
                    return GetSymbol(semantic);

                var attribute = node as AttributeSyntax;
                if (attribute != null)
                    return GetSymbol(attribute);

                var expression = node as ExpressionSyntax;
                if (expression != null)
                    return GetSymbol(expression);

                return null;
            }

            var symbol = getSymbol();
            return symbol != null
                ? new SymbolInfo(symbol)
                : SymbolInfo.None;
        }

        public Symbol GetSymbol(IdentifierDeclarationNameSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundName;
            return result?.Symbol;
        }

        public Symbol GetSymbol(SemanticSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundSemantic;
            return result?.SemanticSymbol;
        }

        public Symbol GetSymbol(AttributeSyntax syntax)
        {
            var result = _bindingResult.GetBoundNode(syntax) as BoundAttribute;
            return result?.AttributeSymbol;
        }

        public Symbol GetSymbol(ExpressionSyntax expression)
        {
            if (expression is IdentifierNameSyntax && expression.Parent.Kind == SyntaxKind.FunctionInvocationExpression)
                expression = (ExpressionSyntax)expression.Parent;

            var boundExpression = GetBoundExpression(expression);
            return boundExpression == null ? null : GetSymbol(boundExpression);
        }

        private static Symbol GetSymbol(BoundExpression expression)
        {
            switch (expression.Kind)
            {
                case BoundNodeKind.VariableExpression:
                    return GetSymbol((BoundVariableExpression)expression);
                case BoundNodeKind.NumericConstructorInvocationExpression:
                    return GetSymbol((BoundNumericConstructorInvocationExpression)expression);
                case BoundNodeKind.FunctionInvocationExpression:
                    return GetSymbol((BoundFunctionInvocationExpression)expression);
                case BoundNodeKind.MethodInvocationExpression:
                    return GetSymbol((BoundMethodInvocationExpression)expression);
                case BoundNodeKind.FieldExpression:
                    return GetSymbol((BoundFieldExpression)expression);
                case BoundNodeKind.Name:
                    return GetSymbol((BoundName)expression);
                case BoundNodeKind.IntrinsicGenericMatrixType:
                case BoundNodeKind.IntrinsicGenericVectorType:
                case BoundNodeKind.IntrinsicMatrixType:
                case BoundNodeKind.IntrinsicObjectType:
                case BoundNodeKind.IntrinsicScalarType:
                case BoundNodeKind.IntrinsicVectorType:
                    return GetSymbol((BoundType)expression);
                case BoundNodeKind.BoundToggleExpression:
                    return GetSymbol((BoundToggleExpression)expression);
                default:
                    // TODO: More bound expression types.
                    return null;
            }
        }

        private static Symbol GetSymbol(BoundToggleExpression expression)
        {
            return expression.Symbol;
        }

        private static Symbol GetSymbol(BoundVariableExpression expression)
        {
            return expression.Symbol;
        }

        private static Symbol GetSymbol(BoundNumericConstructorInvocationExpression expression)
        {
            return expression.Symbol;
        }

        private static Symbol GetSymbol(BoundFunctionInvocationExpression expression)
        {
            return expression.Symbol;
        }

        private static Symbol GetSymbol(BoundMethodInvocationExpression expression)
        {
            return expression.Symbol;
        }

        private static Symbol GetSymbol(BoundFieldExpression expression)
        {
            return expression.Field;
        }

        private static Symbol GetSymbol(BoundType expression)
        {
            return expression.TypeSymbol;
        }

        private static Symbol GetSymbol(BoundName expression)
        {
            return expression.Symbol;
        }

        public override TypeInfo GetTypeInfo(SyntaxNodeBase node)
        {
            var expression = node as ExpressionSyntax;
            if (expression != null)
            {
                var expressionType = GetExpressionType(expression);
                return expressionType != null
                    ? new TypeInfo(expressionType, expressionType)
                    : TypeInfo.None;
            }

            return TypeInfo.None;
        }

        public TypeSymbol GetExpressionType(ExpressionSyntax expression)
        {
            var boundExpression = GetBoundExpression(expression);
            return boundExpression?.Type;
        }

        private BoundExpression GetBoundExpression(ExpressionSyntax expression)
        {
            return _bindingResult.GetBoundNode(expression) as BoundExpression;
        }

        //public override IAliasSymbol GetAliasSymbol(SyntaxNodeBase node)
        //{
        //    var nameSyntax = node as IdentifierNameSyntax;
        //    return nameSyntax == null ? null : GetAliasInfo(nameSyntax);
        //}

        public override IEnumerable<Diagnostic> GetDiagnostics()
        {
            return _bindingResult.Diagnostics;
        }

        public override IEnumerable<ISymbol> LookupSymbols(SourceLocation position)
        {
            var node = FindClosestNodeWithBinder(_bindingResult.Root, position);
            var binder = node == null ? null : _bindingResult.GetBinder(node);
            return binder == null
                ? Enumerable.Empty<Symbol>()
                : LookupSymbols(binder);
        }

        private static IEnumerable<Symbol> LookupSymbols(Binder binder)
        {
            // NOTE: We want to only show the *available* symbols. That means, we need to
            //       hide symbols from the parent binder that have same name as the ones
            //       from a nested binder.
            //
            //       We do this by simply recording which names we've already seen.
            //       Please note that we *do* want to see duplicate names within the
            //       *same* binder.

            var allNames = new HashSet<string>();

            while (binder != null)
            {
                var localNames = new HashSet<string>();
                var localSymbols = binder.LocalSymbols
                    .SelectMany(x => x.Value)
                    .Where(s => !string.IsNullOrEmpty(s.Name));

                foreach (var symbol in localSymbols)
                {
                    if (!allNames.Contains(symbol.Name))
                    {
                        localNames.Add(symbol.Name);
                        yield return symbol;
                    }
                }

                allNames.UnionWith(localNames);
                binder = binder.Parent;
            }
        }

        private SyntaxNode FindClosestNodeWithBinder(SyntaxNode root, SourceLocation position)
        {
            var token = root.FindTokenContext(position);
            return (from n in token.Parent.AncestorsAndSelf().Cast<SyntaxNode>()
                let bc = _bindingResult.GetBinder(n)
                where bc != null
                select n).FirstOrDefault();
        }
    }
}