namespace PO2.Geometry.Picking
{
    internal sealed class ObjectPickCacheState
    {
        internal TrianglePickCache Cache;
        internal TrianglePickCache RefitSource;
        internal bool Failed;
        internal uint Revision;
        internal long LastUsed;
        private object m_Source;
        private uint m_Position;
        private uint m_FullGeometry;
        private uint m_Topology;
        private uint m_Membership;

        internal void Observe(object source, uint position, uint fullGeometry, uint topology, uint membership)
        {
            if (ReferenceEquals(m_Source, source) && m_Position == position && m_FullGeometry == fullGeometry && m_Topology == topology && m_Membership == membership)
            {
                return;
            }

            bool canRefit = ReferenceEquals(m_Source, source) && m_FullGeometry == fullGeometry && m_Topology == topology && m_Membership == membership;
            RefitSource = canRefit ? Cache ?? RefitSource : null;
            Cache = null;
            Failed = false;
            Revision++;
            m_Source = source;
            m_Position = position;
            m_FullGeometry = fullGeometry;
            m_Topology = topology;
            m_Membership = membership;
        }

        internal long RetainedBytes =>
            (Cache ?? RefitSource)?.RetainedBytes ?? 0L;

        internal void Release()
        {
            Revision++;
            Cache = null;
            RefitSource = null;
            Failed = false;
        }
    }
}