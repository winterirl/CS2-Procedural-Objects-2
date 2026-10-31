namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        internal void PrepareForWorldChange()
        {
            PrepareRuntimeSelectionInvalidation();
            // Pending work belongs to the old map, even if its tool is no longer active.
            // Drop its publication state before the new map can reuse runtime object IDs.
            ResetTopologyEdit();
            ResetCageDrag();
            AbandonPendingAssetConversion();
            m_ManagementWorkspaceActive = false;
        }
    }
}
