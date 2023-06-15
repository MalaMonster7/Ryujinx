using Ryujinx.Graphics.Shader.IntermediateRepresentation;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ryujinx.Graphics.Shader.Translation
{
    class ShaderDefinitions
    {
        public ShaderStage Stage { get; }

        public int ComputeLocalSizeX { get; }
        public int ComputeLocalSizeY { get; }
        public int ComputeLocalSizeZ { get; }

        public bool TessCw { get; }
        public TessPatchType TessPatchType { get; }
        public TessSpacing TessSpacing { get; }

        public bool GpPassthrough { get; }
        public bool LastInVertexPipeline { get; set; }

        public int ThreadsPerInputPrimitive { get; }

        public InputTopology InputTopology { get; }
        public OutputTopology OutputTopology { get; }

        public int MaxOutputVertices { get; }

        public bool DualSourceBlend { get; }
        public bool EarlyZForce { get; }

        public ImapPixelType[] ImapTypes { get; }
        public bool IaIndexing { get; private set; }
        public bool OaIndexing { get; private set; }

        public int OmapTargets { get; }
        public bool OmapSampleMask { get; }
        public bool OmapDepth { get; }

        public bool OriginUpperLeft { get; }

        public bool TransformFeedbackEnabled { get; }

        private TransformFeedbackOutput[] _transformFeedbackOutputs;

        readonly struct TransformFeedbackVariable : IEquatable<TransformFeedbackVariable>
        {
            public IoVariable IoVariable { get; }
            public int Location { get; }
            public int Component { get; }

            public TransformFeedbackVariable(IoVariable ioVariable, int location = 0, int component = 0)
            {
                IoVariable = ioVariable;
                Location = location;
                Component = component;
            }

            public override bool Equals(object other)
            {
                return other is TransformFeedbackVariable tfbVar && Equals(tfbVar);
            }

            public bool Equals(TransformFeedbackVariable other)
            {
                return IoVariable == other.IoVariable &&
                    Location == other.Location &&
                    Component == other.Component;
            }

            public override int GetHashCode()
            {
                return (int)IoVariable | (Location << 8) | (Component << 16);
            }

            public override string ToString()
            {
                return $"{IoVariable}.{Location}.{Component}";
            }
        }

        private readonly Dictionary<TransformFeedbackVariable, TransformFeedbackOutput> _transformFeedbackDefinitions;

        private readonly AttributeType[] _attributeTypes;
        private readonly AttributeType[] _fragmentOutputTypes;

        public ShaderDefinitions(ShaderStage stage)
        {
            Stage = stage;
        }

        public ShaderDefinitions(
            ShaderStage stage,
            int computeLocalSizeX,
            int computeLocalSizeY,
            int computeLocalSizeZ)
        {
            Stage = stage;
            ComputeLocalSizeX = computeLocalSizeX;
            ComputeLocalSizeY = computeLocalSizeY;
            ComputeLocalSizeZ = computeLocalSizeZ;
        }

        public ShaderDefinitions(
            ShaderStage stage,
            bool gpPassthrough,
            int threadsPerInputPrimitive,
            InputTopology inputTopology,
            OutputTopology outputTopology,
            int maxOutputVertices)
        {
            Stage = stage;
            GpPassthrough = gpPassthrough;
            ThreadsPerInputPrimitive = threadsPerInputPrimitive;
            InputTopology = inputTopology;
            OutputTopology = outputTopology;
            MaxOutputVertices = maxOutputVertices;
        }

        public ShaderDefinitions(
            ShaderStage stage,
            bool tessCw,
            TessPatchType tessPatchType,
            TessSpacing tessSpacing,
            bool gpPassthrough,
            int threadsPerInputPrimitive,
            InputTopology inputTopology,
            OutputTopology outputTopology,
            int maxOutputVertices,
            bool dualSourceBlend,
            bool earlyZForce,
            ImapPixelType[] imapTypes,
            int omapTargets,
            bool omapSampleMask,
            bool omapDepth,
            bool originUpperLeft,
            bool transformFeedbackEnabled,
            ulong transformFeedkbackVecMap,
            TransformFeedbackOutput[] transformFeedbackOutputs,
            AttributeType[] attributeTypes,
            AttributeType[] fragmentOutputTypes)
        {
            Stage = stage;
            TessCw = tessCw;
            TessPatchType = tessPatchType;
            TessSpacing = tessSpacing;
            GpPassthrough = gpPassthrough;
            ThreadsPerInputPrimitive = threadsPerInputPrimitive;
            InputTopology = inputTopology;
            OutputTopology = outputTopology;
            MaxOutputVertices = maxOutputVertices;
            DualSourceBlend = dualSourceBlend;
            EarlyZForce = earlyZForce;
            ImapTypes = imapTypes;
            OmapTargets = omapTargets;
            OmapSampleMask = omapSampleMask;
            OmapDepth = omapDepth;
            OriginUpperLeft = originUpperLeft;
            LastInVertexPipeline = stage < ShaderStage.Fragment;
            TransformFeedbackEnabled = transformFeedbackEnabled;
            _transformFeedbackOutputs = transformFeedbackOutputs;
            _transformFeedbackDefinitions = new Dictionary<TransformFeedbackVariable, TransformFeedbackOutput>();
            _attributeTypes = attributeTypes;
            _fragmentOutputTypes = fragmentOutputTypes;

            while (transformFeedkbackVecMap != 0)
            {
                int vecIndex = BitOperations.TrailingZeroCount(transformFeedkbackVecMap);

                for (int subIndex = 0; subIndex < 4; subIndex++)
                {
                    int wordOffset = vecIndex * 4 + subIndex;
                    int byteOffset = wordOffset * 4;

                    if (transformFeedbackOutputs[wordOffset].Valid)
                    {
                        IoVariable ioVariable = Instructions.AttributeMap.GetIoVariable(this, byteOffset, out int location);
                        int component = 0;

                        if (HasPerLocationInputOrOutputComponent(ioVariable, location, subIndex, isOutput: true))
                        {
                            component = subIndex;
                        }

                        var transformFeedbackVariable = new TransformFeedbackVariable(ioVariable, location, component);
                        _transformFeedbackDefinitions.TryAdd(transformFeedbackVariable, transformFeedbackOutputs[wordOffset]);
                    }
                }

                transformFeedkbackVecMap &= ~(1UL << vecIndex);
            }
        }

        public void EnableInputIndexing()
        {
            IaIndexing = true;
        }

        public void EnableOutputIndexing()
        {
            OaIndexing = true;
        }

        public TransformFeedbackOutput[] GetTransformFeedbackOutputs()
        {
            if (!HasTransformFeedbackOutputs())
            {
                return null;
            }

            return _transformFeedbackOutputs;
        }

        public bool TryGetTransformFeedbackOutput(IoVariable ioVariable, int location, int component, out TransformFeedbackOutput transformFeedbackOutput)
        {
            if (!HasTransformFeedbackOutputs())
            {
                transformFeedbackOutput = default;
                return false;
            }

            var transformFeedbackVariable = new TransformFeedbackVariable(ioVariable, location, component);
            return _transformFeedbackDefinitions.TryGetValue(transformFeedbackVariable, out transformFeedbackOutput);
        }

        private bool HasTransformFeedbackOutputs()
        {
            return TransformFeedbackEnabled && (LastInVertexPipeline || Stage == ShaderStage.Fragment);
        }

        public bool HasTransformFeedbackOutputs(bool isOutput)
        {
            return TransformFeedbackEnabled && ((isOutput && LastInVertexPipeline) || (!isOutput && Stage == ShaderStage.Fragment));
        }

        public bool HasPerLocationInputOrOutput(IoVariable ioVariable, bool isOutput)
        {
            if (ioVariable == IoVariable.UserDefined)
            {
                return (!isOutput && !IaIndexing) || (isOutput && !OaIndexing);
            }

            return ioVariable == IoVariable.FragmentOutputColor;
        }

        public bool HasPerLocationInputOrOutputComponent(IoVariable ioVariable, int location, int component, bool isOutput)
        {
            if (ioVariable != IoVariable.UserDefined || !HasTransformFeedbackOutputs(isOutput))
            {
                return false;
            }

            return GetTransformFeedbackOutputComponents(location, component) == 1;
        }

        public TransformFeedbackOutput GetTransformFeedbackOutput(int wordOffset)
        {
            return _transformFeedbackOutputs[wordOffset];
        }

        public TransformFeedbackOutput GetTransformFeedbackOutput(int location, int component)
        {
            return GetTransformFeedbackOutput((AttributeConsts.UserAttributeBase / 4) + location * 4 + component);
        }

        public int GetTransformFeedbackOutputComponents(int location, int component)
        {
            int baseIndex = (AttributeConsts.UserAttributeBase / 4) + location * 4;
            int index = baseIndex + component;
            int count = 1;

            for (; count < 4; count++)
            {
                ref var prev = ref _transformFeedbackOutputs[baseIndex + count - 1];
                ref var curr = ref _transformFeedbackOutputs[baseIndex + count];

                int prevOffset = prev.Offset;
                int currOffset = curr.Offset;

                if (!prev.Valid || !curr.Valid || prevOffset + 4 != currOffset)
                {
                    break;
                }
            }

            if (baseIndex + count <= index)
            {
                return 1;
            }

            return count;
        }

        public AggregateType GetFragmentOutputColorType(int location)
        {
            return AggregateType.Vector4 | _fragmentOutputTypes[location].ToAggregateType();
        }

        public AggregateType GetUserDefinedType(int location, bool isOutput)
        {
            if ((!isOutput && IaIndexing) || (isOutput && OaIndexing))
            {
                return AggregateType.Array | AggregateType.Vector4 | AggregateType.FP32;
            }

            AggregateType type = AggregateType.Vector4;

            if (Stage == ShaderStage.Vertex && !isOutput)
            {
                type |= _attributeTypes[location].ToAggregateType();
            }
            else
            {
                type |= AggregateType.FP32;
            }

            return type;
        }
    }
}