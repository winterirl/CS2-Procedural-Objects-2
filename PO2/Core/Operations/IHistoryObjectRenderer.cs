using System;
using System.Threading;
using System.Threading.Tasks;

namespace PO2.Core.Operations
{
    internal interface IHistoryObjectRenderer : IDisposable
    {
        ProceduralObject Object { get; }
        Task Drain { get; }
        bool Poll(CancellationToken token);
        void Publish();
    }
}