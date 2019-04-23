using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChakraCoreHost.Tasks
{
    public class ActionTaskItem : TaskItem
    {
        private Action action;

        public ActionTaskItem(Action action)
        {
            this.action = action;
        }

        public override void Run()
        {
            this.action.Invoke();
        }
    }
}