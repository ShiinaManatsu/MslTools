using System.Collections.Immutable;
using ShaderTools.CodeAnalysis.Hlsl.Symbols;

namespace ShaderTools.CodeAnalysis.Hlsl.Binding.BoundNodes
{
    internal sealed class BoundMessiahTechnique : BoundNode
    {
        public MessiahTechniqueSymbol TechniqueSymbol { get; }
        public ImmutableArray<BoundExpression> Expressions { get; }

        public BoundMessiahTechnique(MessiahTechniqueSymbol techniqueSymbol, ImmutableArray<BoundExpression> expressions)
            : base(BoundNodeKind.MessiahTechnique)
        {
            TechniqueSymbol = techniqueSymbol;
            Expressions = expressions;
        }
    }
}