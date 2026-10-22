using System;
using Colossal.UI.Binding;
using PO2.Core;
using PO2.Core.Lod;
using PO2.Rendering.Lod;

namespace PO2.Interface.Models
{
    internal sealed class LodPanelState : IJsonWritable, IEquatable<LodPanelState>
    {
        internal static readonly LodPanelState s_Empty = new LodPanelState();
        private readonly int m_ObjectId;
        private readonly string m_Session;
        private readonly bool m_Available, m_Editable, m_Dirty, m_CanGenerate, m_CanApply;
        private readonly LodProfile m_Profile;
        private readonly LodTransitionSettings m_Distances;
        private readonly LodPreviewStatus m_Status;
        private readonly LodPreviewDisplay m_Display;
        private readonly LodObjectDiagnostics m_Diagnostics;
        private readonly LodAutomaticChain m_Chain;
        private readonly double m_SourceTriangles;
        private readonly int m_Progress;
        private readonly bool m_HasResult;
        private readonly bool m_CanPreviewLod1, m_CanPreviewLod2;

        private LodPanelState()
        {
        }

        internal LodPanelState(ProceduralObject obj, LodPreviewSession session, bool available, bool blocked, float globalLod1, float globalLod2)
        {
            m_ObjectId = obj.Id;
            m_Session = session?.SessionId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            m_Available = available && session != null;
            m_Profile = session != null ? session.DraftProfile : obj.LodProfile;
            m_Distances = LodProfile.ResolveTransitions(m_Profile, globalLod1, globalLod2);
            m_Status = session?.Status ?? LodPreviewStatus.Idle;
            m_Display = session?.Display ?? LodPreviewDisplay.Original;
            m_Dirty = !Equals(m_Profile, obj.LodProfile);
            m_Editable = m_Available && !blocked && !obj.IsLocked;
            m_CanGenerate = m_Available && !blocked && !session.IsBusy && (!obj.IsLocked || !m_Dirty);
            m_CanApply = m_Editable && m_Dirty && session.Status == LodPreviewStatus.Ready && session.HasCompletedGeneration;
            m_HasResult = session?.Status == LodPreviewStatus.Ready && session.HasCompletedGeneration;
            m_CanPreviewLod1 = session?.CanDisplay(LodPreviewDisplay.Lod1) == true;
            m_CanPreviewLod2 = session?.CanDisplay(LodPreviewDisplay.Lod2) == true;
            m_Diagnostics = session?.Diagnostics ?? default;
            m_Chain = session?.AutomaticChain ?? default;
            foreach (var part in obj.Parts)
            {
                m_SourceTriangles += part.Mesh.Triangles.Length / 3;
            }

            if (session != null)
            {
                if (session.Status == LodPreviewStatus.Capturing && session.CaptureBytes > 0)
                {
                    m_Progress = (int)(100.0 * session.CapturedBytes / session.CaptureBytes);
                }
                else if (session.Status == LodPreviewStatus.Uploading && session.UploadMeshes > 0)
                {
                    m_Progress = 100 * session.UploadedMeshes / session.UploadMeshes;
                }
            }
        }

        public bool Equals(LodPanelState other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return other != null && m_ObjectId == other.m_ObjectId && m_Session == other.m_Session && m_Available == other.m_Available && m_Editable == other.m_Editable && m_Dirty == other.m_Dirty && m_CanGenerate == other.m_CanGenerate && m_CanApply == other.m_CanApply && Equals(m_Profile, other.m_Profile) && m_Distances.Equals(other.m_Distances) && m_Status == other.m_Status && m_Display == other.m_Display && m_Progress == other.m_Progress && m_SourceTriangles == other.m_SourceTriangles && m_HasResult == other.m_HasResult && m_CanPreviewLod1 == other.m_CanPreviewLod1 && m_CanPreviewLod2 == other.m_CanPreviewLod2 && m_Diagnostics.Equals(other.m_Diagnostics) && m_Chain.Equals(other.m_Chain);
        }

        public override bool Equals(object obj) => Equals(obj as LodPanelState);
        public override int GetHashCode() => m_ObjectId;

        public void Write(IJsonWriter writer)
        {
            if (m_ObjectId == 0)
            {
                writer.WriteNull();
                return;
            }

            LodProfile profile = LodProfile.Resolve(m_Profile);
            writer.TypeBegin("PO2.LodPanelState");
            writer.PropertyName("objectId");
            writer.Write(m_ObjectId);
            writer.PropertyName("session");
            writer.Write(m_Session);
            writer.PropertyName("available");
            writer.Write(m_Available);
            writer.PropertyName("editable");
            writer.Write(m_Editable);
            writer.PropertyName("dirty");
            writer.Write(m_Dirty);
            writer.PropertyName("canGenerate");
            writer.Write(m_CanGenerate);
            writer.PropertyName("canApply");
            writer.Write(m_CanApply);
            writer.PropertyName("inherit");
            writer.Write(m_Profile == null);
            writer.PropertyName("inheritDistances");
            writer.Write(m_Profile?.Transitions == null);
            writer.PropertyName("distance1");
            writer.Write(m_Distances.Lod1Distance);
            writer.PropertyName("distance2");
            writer.Write(m_Distances.Lod2Distance);
            LodPresentation.GetTransitionHeights(m_Distances, out float firstHeight, out float secondHeight);
            writer.PropertyName("transitionsAdjusted");
            writer.Write(secondHeight < 0.045f / m_Distances.Lod2Distance);
            writer.PropertyName("height1");
            writer.Write(firstHeight);
            writer.PropertyName("height2");
            writer.Write(secondHeight);
            writer.PropertyName("status");
            writer.Write((int)m_Status);
            writer.PropertyName("display");
            writer.Write((int)m_Display);
            writer.PropertyName("progress");
            writer.Write(m_Progress);
            writer.PropertyName("sourceTriangles");
            writer.Write(m_SourceTriangles);
            writer.PropertyName("hasResult");
            writer.Write(m_HasResult);
            writer.PropertyName("lod1");
            WriteLevel(writer, profile.Lod1, 1);
            writer.PropertyName("lod2");
            WriteLevel(writer, profile.Lod2, 2);
            writer.TypeEnd();
        }

        private void WriteLevel(IJsonWriter writer, LodLevelSettings level, int index)
        {
            bool first = index == 1;
            writer.TypeBegin("PO2.LodLevel");
            writer.PropertyName("enabled");
            writer.Write(level.Enabled);
            writer.PropertyName("canPreview");
            writer.Write(first ? m_CanPreviewLod1 : m_CanPreviewLod2);
            writer.PropertyName("method");
            writer.Write((int)level.Method);
            writer.PropertyName("ratio");
            writer.Write(level.TriangleRatio);
            writer.PropertyName("cap");
            writer.Write(level.TriangleCap ?? 0);
            writer.PropertyName("error");
            writer.Write(level.MaximumRelativeError);
            writer.PropertyName("prune");
            writer.Write(level.PruneRelativeError);
            writer.PropertyName("actual");
            writer.Write(first ? m_Diagnostics.Lod1Triangles : m_Diagnostics.Lod2Triangles);
            writer.PropertyName("target");
            writer.Write(first ? m_Diagnostics.Lod1TargetTriangles : m_Diagnostics.Lod2TargetTriangles);
            writer.PropertyName("targetMet");
            writer.Write(first ? m_Diagnostics.Lod1BudgetMet : m_Diagnostics.Lod2BudgetMet);
            writer.PropertyName("status");
            writer.Write((int)(first ? m_Chain.Lod1Status : m_Chain.Lod2Status));
            writer.PropertyName("inAuto");
            writer.Write(first ? m_Chain.IncludeLod1 : m_Chain.IncludeLod2);
            writer.TypeEnd();
        }
    }
}