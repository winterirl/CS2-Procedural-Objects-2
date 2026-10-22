using System.Threading;
using System.Threading.Tasks;
using PO2.History;
using PO2.Localization;

namespace PO2.History.Actions
{
    internal abstract class HistoryCaptureAction : IHistoryAction
    {
        public LocalizedText Description { get; }
        public bool HasChanges { get; protected set; }
        public abstract HistoryFootprint Footprint { get; }

        protected HistoryCaptureAction(LocalizedText description)
        {
            Description = description;
        }

        internal abstract void Seal(HistoryContext context);
        internal abstract void RestoreCapture(HistoryContext context);

        public virtual Task<HistoryResult> Prepare(HistoryDirection direction, CancellationToken cancellation) =>
            Task.FromResult(HistoryResult.Ok);

        public abstract HistoryResult Replay(HistoryDirection direction, HistoryContext context);
        public abstract void Dispose();
    }

    internal abstract class HistorySwapAction<T> : HistoryCaptureAction where T : class
    {
        private T m_Opposite;
        private ulong m_Expected;
        protected T Opposite => m_Opposite;

        protected HistorySwapAction(LocalizedText description, T before) : base(description)
        {
            m_Opposite = before;
        }

        protected abstract T Capture(HistoryContext context);
        protected abstract ulong Fingerprint(T state);
        protected abstract void Apply(HistoryContext context, T state);

        internal override void Seal(HistoryContext context)
        {
            m_Expected = Fingerprint(Capture(context));
            HasChanges = m_Expected != Fingerprint(m_Opposite);
        }

        internal override void RestoreCapture(HistoryContext context)
        {
            if (Fingerprint(Capture(context)) != Fingerprint(m_Opposite)) Apply(context, m_Opposite);
        }

        public override HistoryResult Replay(HistoryDirection direction, HistoryContext context)
        {
            T current = Capture(context);
            if (Fingerprint(current) != m_Expected)
            {
                return new HistoryResult(false, Description + ": target state no longer matches.", true);
            }

            // Concrete setters validate/allocate before making any changes.
            ulong expected = Fingerprint(m_Opposite);
            Apply(context, m_Opposite);
            m_Expected = expected;
            m_Opposite = current;
            return HistoryResult.Ok;
        }

        public override void Dispose()
        {
            m_Opposite = null;
        }
    }
}