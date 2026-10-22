using Game;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;
using PO2.Tools.Snapping;

namespace PO2.Tools.ObjectTool
{
    public partial class ProceduralObjectToolSystem
    {
        private readonly SnapSettings m_SnapSettings = new SnapSettings();

        public bool SnapMagnetEnabled =>
            m_SnapSettings.MagnetEnabled;

        public bool SnapIncrementEnabled =>
            m_SnapSettings.IncrementEnabled;

        public bool SnapPOVerticesEnabled =>
            m_SnapSettings.POVerticesEnabled;

        public bool SnapPOEdgesEnabled =>
            m_SnapSettings.POEdgesEnabled;

        public bool SnapGameSurfacesEnabled =>
            m_SnapSettings.GameSurfacesEnabled;

        public float MoveSnapIncrement =>
            m_SnapSettings.MoveIncrement;

        public float PrecisionMoveSnapIncrement =>
            m_SnapSettings.PrecisionMoveIncrement;

        public float RotationSnapIncrement =>
            m_SnapSettings.RotationIncrement;

        public float PrecisionRotationSnapIncrement =>
            m_SnapSettings.PrecisionRotationIncrement;

        public float ScaleSnapIncrement =>
            m_SnapSettings.ScaleIncrement;

        public float PrecisionScaleSnapIncrement =>
            m_SnapSettings.PrecisionScaleIncrement;

        public void ToggleSnapMagnet()
        {
            m_SnapSettings.ToggleMagnet();
        }

        public void ToggleSnapIncrement()
        {
            m_SnapSettings.ToggleIncrement();
        }

        public void ToggleSnapPOVertices()
        {
            m_SnapSettings.TogglePOVertices();
        }

        public void ToggleSnapPOEdges()
        {
            m_SnapSettings.TogglePOEdges();
        }

        public void ToggleSnapGameSurfaces()
        {
            m_SnapSettings.ToggleGameSurfaces();
        }

        public void SetMoveSnapIncrement(float value)
        {
            m_SnapSettings.SetMoveIncrement(value);
        }

        public void SetPrecisionMoveSnapIncrement(float value)
        {
            m_SnapSettings.SetPrecisionMoveIncrement(value);
        }

        public void SetRotationSnapIncrement(float value)
        {
            m_SnapSettings.SetRotationIncrement(value);
        }

        public void SetPrecisionRotationSnapIncrement(float value)
        {
            m_SnapSettings.SetPrecisionRotationIncrement(value);
        }

        public void SetScaleSnapIncrement(float value)
        {
            m_SnapSettings.SetScaleIncrement(value);
        }

        public void SetPrecisionScaleSnapIncrement(float value)
        {
            m_SnapSettings.SetPrecisionScaleIncrement(value);
        }

        public void ResetSnapIncrements()
        {
            m_SnapSettings.ResetIncrements();
        }
    }
}