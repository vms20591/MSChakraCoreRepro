using ChakraHost.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChakraCoreHost.Tasks
{
    public class JsTaskItem : TaskItem
    {
        private JavaScriptValue taskFunc;

        public JsTaskItem(JavaScriptValue taskFunc)
        {
            this.taskFunc = taskFunc;

            // We need to keep the object alive in the Chakra GC.
            this.taskFunc.AddRef();
        }

        ~JsTaskItem()
        {
            this.Dispose(false);
        }

        public override void Run()
        {
            taskFunc.CallFunction(JavaScriptValue.GlobalObject);
        }

        protected override void Dispose(bool disposing)
        {
            // We need to release the object so that the Chakra GC can reclaim it.
            taskFunc.Release();

            base.Dispose(disposing);

            if (disposing)
            {
                // No need to finalize the object if we've been disposed.
                GC.SuppressFinalize(this);
            }
        }
    }
}