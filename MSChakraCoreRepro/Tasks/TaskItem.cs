using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChakraCoreHost.Tasks
{
    public abstract class TaskItem : IDisposable
    {
        public abstract void Run();

        protected virtual void Dispose(bool disposing)
        {
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
    }
}