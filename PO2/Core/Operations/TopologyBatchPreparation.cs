using System;
using System.Diagnostics;
using System.Threading.Tasks;
using PO2.Localization;

namespace PO2.Core.Operations
{
    // All parts share one owner-thread time budget. Cheap upload/setup steps
    // can finish in the same update; unfinished worker tasks yield immediately.
    internal sealed class TopologyBatchPreparation : IDisposable
    {
        private readonly Func<int, TopologyPreparation> m_Create;
        private int m_Next;
        internal const double kOwnerMilliseconds = 2;
        internal readonly TopologyPreparation[] Parts;
        internal Task Drain { get; private set; } = Task.CompletedTask;
        internal bool IsReady => m_Next == Parts.Length;
        internal LocalizedText Stage => IsReady ? LocalizedText.For("PO2.Object.Mirror.Stage.Publishing") : LocalizedText.For("PO2.Object.Mirror.Stage.PartProgress", ("stage", Parts[m_Next]?.Stage ?? LocalizedText.For("PO2.Edit.Topology.Stage.Capturing")), ("index", m_Next + 1), ("count", Parts.Length));

        internal TopologyBatchPreparation(int count, Func<int, TopologyPreparation> create)
        {
            Parts = new TopologyPreparation[count];
            m_Create = create;
        }

        internal bool Poll()
        {
            if (IsReady) return true;
            long start = Stopwatch.GetTimestamp();
            long deadline = start + (long)(Stopwatch.Frequency * kOwnerMilliseconds / 1000d);
            do
            {
                if (Parts[m_Next] == null)
                {
                    Parts[m_Next] = m_Create(m_Next);
                }
                else
                {
                    var part = Parts[m_Next];
                    if (part.Poll(deadline))
                    {
                        m_Next++;
                        if (IsReady)
                        {
                            return true;
                        }
                    }
                    else if (!part.CanContinue) return false;
                }
            } while (Stopwatch.GetTimestamp() < deadline);

            return false;
        }

        internal bool Publish(Func<bool> record) => TopologyPreparation.PublishAll(Parts, record);

        public void Dispose()
        {
            var drains = new Task[Parts.Length];
            for (int i = 0; i < Parts.Length; i++)
            {
                Parts[i]?.Dispose();
                drains[i] = Parts[i]?.Drain ?? Task.CompletedTask;
            }

            Drain = Task.WhenAll(drains);
        }
    }
}