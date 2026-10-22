using System;

namespace PO2.Core.Operations
{
    internal interface IArrayRemoval : IDisposable
    {
        void Adopt();
        void Restore();
    }
}