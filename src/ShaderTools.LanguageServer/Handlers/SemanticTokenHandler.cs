using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ShaderTools.CodeAnalysis.Classification;

#pragma warning disable 618
namespace ShaderTools.LanguageServer.Handlers
{
    internal class SemanticTokenHandler : SemanticTokensHandlerBase
    {
        static class HlslClassificationTypeNames
        {
            public const string Punctuation = "Hlsl.Punctuation";
            public const string Semantic = "Hlsl.Semantic";
            public const string PackOffset = "Hlsl.PackOffset";
            public const string RegisterLocation = "Hlsl.RegisterLocation";
            public const string NamespaceIdentifier = "Hlsl.Namespace";
            public const string GlobalVariableIdentifier = "Hlsl.GlobalVariable";
            public const string FieldIdentifier = "Hlsl.Field";
            public const string LocalVariableIdentifier = "Hlsl.LocalVariable";
            public const string ConstantBufferVariableIdentifier = "Hlsl.ConstantBufferVariable";
            public const string ParameterIdentifier = "Hlsl.Parameter";
            public const string FunctionIdentifier = "Hlsl.Function";
            public const string MethodIdentifier = "Hlsl.Method";
            public const string ClassIdentifier = "Hlsl.Class";
            public const string StructIdentifier = "Hlsl.Struct";
            public const string InterfaceIdentifier = "Hlsl.Interface";
            public const string ConstantBufferIdentifier = "Hlsl.ConstantBuffer";
            public const string MacroIdentifier = "Hlsl.Macro";
            public const string ToggleIdentifier = "Hlsl.Toggle";
            public const string AnnotationIdentifier = "Hlsl.AnnotationIdentifier";
            public const string PropertyIdentifier = "Hlsl.PropertyIdentifier";
        }

        public static readonly SemanticTokenModifier ModifierLocal = new SemanticTokenModifier("local");
        public static readonly SemanticTokenModifier ModifierGlobal = new SemanticTokenModifier("global");
        public static readonly SemanticTokenModifier ModifierField = new SemanticTokenModifier("field");
        public static readonly SemanticTokenType TokenAnnotation = new SemanticTokenType("annotation");

        public static readonly List<SemanticTokenModifier> AnnotationColors = new() {
            new SemanticTokenModifier("colorRed"),
            new SemanticTokenModifier("colorBlue"),
            new SemanticTokenModifier("colorGreen"),
            new SemanticTokenModifier("colorYellow"),
            new SemanticTokenModifier("colorPurple"),
            new SemanticTokenModifier("colorOrange"),
            new SemanticTokenModifier("colorCyan"),
            new SemanticTokenModifier("colorPink"),
            new SemanticTokenModifier("colorBrown"),
        };

        static SemanticTokensRegistrationOptions _options = new SemanticTokensRegistrationOptions
        {
            Full = new SemanticTokensCapabilityRequestFull(),
            Legend = new SemanticTokensLegend()
            {
                TokenModifiers = new Container<SemanticTokenModifier>(SemanticTokenModifier.Defaults
                    .Concat(AnnotationColors)
                    .Concat([ModifierLocal, ModifierGlobal, ModifierField])
                ),
                TokenTypes = new Container<SemanticTokenType>(SemanticTokenType.Defaults.Append(TokenAnnotation))
            }
        };

        public static int annoationColorSpinIndex = 0;

        public static (SemanticTokenType, List<SemanticTokenModifier>) GetClassificationType(
            string classificationTypeNames) => classificationTypeNames switch
            {
                HlslClassificationTypeNames.Punctuation => (SemanticTokenType.Label, []),
                HlslClassificationTypeNames.Semantic => (SemanticTokenType.Interface, []),
                HlslClassificationTypeNames.PackOffset => (SemanticTokenType.EnumMember, []),
                HlslClassificationTypeNames.RegisterLocation => (SemanticTokenType.EnumMember, []),
                HlslClassificationTypeNames.NamespaceIdentifier => (SemanticTokenType.Namespace, []),
                HlslClassificationTypeNames.GlobalVariableIdentifier => (SemanticTokenType.Variable, [ModifierGlobal]),
                HlslClassificationTypeNames.FieldIdentifier => (SemanticTokenType.Variable, [ModifierField]),
                HlslClassificationTypeNames.LocalVariableIdentifier => (SemanticTokenType.Variable, []),
                HlslClassificationTypeNames.ParameterIdentifier => (SemanticTokenType.Parameter, []),
                HlslClassificationTypeNames.FunctionIdentifier => (SemanticTokenType.Function, []),
                HlslClassificationTypeNames.MethodIdentifier => (SemanticTokenType.Function, []),
                HlslClassificationTypeNames.ClassIdentifier => (SemanticTokenType.Class, []),
                HlslClassificationTypeNames.StructIdentifier => (SemanticTokenType.Struct, []),
                HlslClassificationTypeNames.InterfaceIdentifier => (SemanticTokenType.Interface, []),
                HlslClassificationTypeNames.ConstantBufferVariableIdentifier => (SemanticTokenType.Variable, [ModifierGlobal]),
                HlslClassificationTypeNames.ConstantBufferIdentifier => (SemanticTokenType.Class, []),
                HlslClassificationTypeNames.MacroIdentifier => (SemanticTokenType.Macro, []),
                HlslClassificationTypeNames.ToggleIdentifier => (SemanticTokenType.Macro, []),
                HlslClassificationTypeNames.AnnotationIdentifier => (TokenAnnotation, [AnnotationColors[(annoationColorSpinIndex++) % AnnotationColors.Count]]),
                "Hlsl.AnnotationIdentifier_0" => (TokenAnnotation, [AnnotationColors[0]]),
                "Hlsl.AnnotationIdentifier_1" => (TokenAnnotation, [AnnotationColors[1]]),
                "Hlsl.AnnotationIdentifier_2" => (TokenAnnotation, [AnnotationColors[2]]),
                "Hlsl.AnnotationIdentifier_3" => (TokenAnnotation, [AnnotationColors[3]]),
                "Hlsl.AnnotationIdentifier_4" => (TokenAnnotation, [AnnotationColors[4]]),
                "Hlsl.AnnotationIdentifier_5" => (TokenAnnotation, [AnnotationColors[5]]),
                "Hlsl.AnnotationIdentifier_6" => (TokenAnnotation, [AnnotationColors[6]]),
                "Hlsl.AnnotationIdentifier_7" => (TokenAnnotation, [AnnotationColors[7]]),
                "Hlsl.AnnotationIdentifier_8" => (TokenAnnotation, [AnnotationColors[8]]),
                HlslClassificationTypeNames.PropertyIdentifier => (SemanticTokenType.Property, []),
                _ => (SemanticTokenType.Label, [])
            };

        private readonly LanguageServerWorkspace _workspace;

        public SemanticTokenHandler(LanguageServerWorkspace workspace, TextDocumentSelector documentSelector)
        {
            _workspace = workspace;
        }


        protected override async Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier,
            CancellationToken cancellationToken)
        {
            var document = _workspace.GetDocument(identifier.TextDocument.Uri);
            var ast = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var classificationService = document?.LanguageServices.GetService<IClassificationService>();

            if (classificationService == null)
            {
                return;
            }

            var syntaxTree = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            var classifiedSpans = new List<ClassifiedSpan>();

            classificationService.AddSemanticClassifications(
                syntaxTree,
                new TextSpan(0, document.SourceText.Length),
                _workspace,
                classifiedSpans,
                cancellationToken);


            foreach (var x in classifiedSpans.DistinctBy(x => x.TextSpan).OrderBy(x => x.TextSpan.Start))
            {
                var content = document.SourceText.GetSubText(x.TextSpan).ToString();
                if (content.Contains(" "))
                {
                    continue;
                }
                if (x.ClassificationType != ClassificationTypeNames.WhiteSpace)
                {
                    var range = Helpers.ToRange(document.SourceText, x.TextSpan);
                    var (semanticTokenType, semanticTokenModifiers) = GetClassificationType(x.ClassificationType);
                    builder.Push(range, semanticTokenType, semanticTokenModifiers);
                }
            }
        }

        protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
            ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
        {
            return Task.FromResult(new SemanticTokensDocument(_options.Legend));
        }

        protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
            SemanticTokensCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return _options;
        }
    }
}
#pragma warning restore 618