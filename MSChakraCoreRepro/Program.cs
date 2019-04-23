using ChakraCoreHost.Tasks;
using ChakraHost.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MSChakraCoreRepro
{
    public static class Program
    {
        private static ManualResetEvent queueEvent = new ManualResetEvent(false);
        private static volatile int outstandingItems = 0;
        private static Queue<TaskItem> taskQueue = new Queue<TaskItem>();
        private static object taskSync = new object();

        private struct CommandLineArguments
        {
            public int ArgumentsStart;
        };

        private static JavaScriptSourceContext currentSourceContext = JavaScriptSourceContext.FromIntPtr(IntPtr.Zero);

        // We have to hold on to the delegates on the managed side of things so that the
        // delegates aren't collected while the script is running.
        private static readonly JavaScriptNativeFunction echoDelegate = Echo;
        private static readonly JavaScriptNativeFunction runScriptDelegate = RunScript;
        private static readonly JavaScriptNativeFunction doSuccessfulWorkDelegate = DoSuccessfulWork;
        private static readonly JavaScriptNativeFunction doUnsuccessfulWorkDelegate = DoUnsuccessfulWork;

        private static void Enqueue(TaskItem item)
        {
            lock (taskSync)
            {
                taskQueue.Enqueue(item);
            }

            queueEvent.Set();
        }

        private static TaskItem Dequeue()
        {
            TaskItem item = null;
            lock (taskSync)
            {
                item = taskQueue.Dequeue();
            }

            queueEvent.Reset();

            return item;
        }

        private static void PumpMessages()
        {
            while (true)
            {
                bool hasTasks = false;
                bool hasOutstandingItems = false;

                lock (taskSync)
                {
                    hasTasks = taskQueue.Count > 0;
                    hasOutstandingItems = outstandingItems > 0;
                }

                if (hasTasks)
                {
                    TaskItem task = Dequeue();
                    task.Run();
                    task.Dispose();
                }
                else if (hasOutstandingItems)
                {
                    queueEvent.WaitOne();
                }
                else
                {
                    break;
                }
            }
        }

        private static void ThrowException(string errorString)
        {
            // We ignore error since we're already in an error state.
            JavaScriptValue errorValue = JavaScriptValue.FromString(errorString);
            JavaScriptValue errorObject = JavaScriptValue.CreateError(errorValue);
            JavaScriptContext.SetException(errorObject);
        }

        private static JavaScriptValue Echo(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            for (uint index = 1; index < argumentCount; index++)
            {
                if (index > 1)
                {
                    Console.Write(" ");
                }

                Console.Write(arguments[index].ConvertToString().ToString());
            }

            Console.WriteLine();

            return JavaScriptValue.Invalid;
        }

        private static JavaScriptValue RunScript(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            if (argumentCount < 2)
            {
                ThrowException("not enough arguments");
                return JavaScriptValue.Invalid;
            }

            //
            // Convert filename.
            //

            string filename = arguments[1].ToString();

            //
            // Load the script from the disk.
            //

            string script = File.ReadAllText(filename);
            if (string.IsNullOrEmpty(script))
            {
                ThrowException("invalid script");
                return JavaScriptValue.Invalid;
            }

            //
            // Run the script.
            //

            return JavaScriptContext.RunScript(script, currentSourceContext++, filename);
        }

        private static async void CallAfterDelay(JavaScriptValue function, int delayInMilliseconds, string value)
        {
            lock (taskSync)
            {
                outstandingItems++;
            }

            await Task.Delay(delayInMilliseconds);

            Enqueue(new ActionTaskItem(() => { function.CallFunction(JavaScriptValue.GlobalObject, JavaScriptValue.FromString(value)); }));

            lock (taskSync)
            {
                outstandingItems--;
            }
        }

        private static JavaScriptValue DoSuccessfulWork(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            JavaScriptValue resolve;
            JavaScriptValue reject;
            JavaScriptValue promise = JavaScriptValue.CreatePromise(out resolve, out reject);

            CallAfterDelay(resolve, 1000, "promise from native code");

            return promise;
        }

        private static JavaScriptValue DoUnsuccessfulWork(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            JavaScriptValue resolve;
            JavaScriptValue reject;
            JavaScriptValue promise = JavaScriptValue.CreatePromise(out resolve, out reject);

            CallAfterDelay(reject, 1000, "promise from native code");

            return promise;
        }

        private static JavaScriptValue ResolveCallback(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            if (argumentCount > 1)
            {
                Console.WriteLine("Resolved: " + arguments[1].ConvertToString().ToString());
            }

            return JavaScriptValue.Invalid;
        }

        private static JavaScriptValue RejectCallback(JavaScriptValue callee, bool isConstructCall, JavaScriptValue[] arguments, ushort argumentCount, IntPtr callbackData)
        {
            if (argumentCount > 1)
            {
                Console.WriteLine("Rejected: " + arguments[1].ConvertToString().ToString());
            }

            return JavaScriptValue.Invalid;
        }

        private static void ContinuePromise(JavaScriptValue promise)
        {
            JavaScriptPropertyId thenId = JavaScriptPropertyId.FromString("then");
            JavaScriptValue thenFunction = promise.GetProperty(thenId);

            JavaScriptValue resolveFunc = JavaScriptValue.CreateFunction(ResolveCallback);
            JavaScriptValue rejectFunc = JavaScriptValue.CreateFunction(RejectCallback);
            JavaScriptValue thenPromise = thenFunction.CallFunction(promise, resolveFunc, rejectFunc);
        }

        private static void DefineHostCallback(JavaScriptValue globalObject, string callbackName, JavaScriptNativeFunction callback, IntPtr callbackData)
        {
            //
            // Get property ID.
            //

            JavaScriptPropertyId propertyId = JavaScriptPropertyId.FromString(callbackName);

            //
            // Create a function
            //

            JavaScriptValue function = JavaScriptValue.CreateFunction(callback, callbackData);

            //
            // Set the property
            //

            globalObject.SetProperty(propertyId, function, true);
        }

        private static JavaScriptContext CreateHostContext(JavaScriptRuntime runtime, string[] arguments, int argumentsStart)
        {
            //
            // Create the context. Note that if we had wanted to start debugging from the very
            // beginning, we would have called JsStartDebugging right after context is created.
            //

            JavaScriptContext context = runtime.CreateContext();

            //
            // Now set the execution context as being the current one on this thread.
            //

            using (new JavaScriptContext.Scope(context))
            {
                //
                // Create the host object the script will use.
                //

                JavaScriptValue hostObject = JavaScriptValue.CreateObject();

                //
                // Get the global object
                //

                JavaScriptValue globalObject = JavaScriptValue.GlobalObject;

                //
                // Get the name of the property ("host") that we're going to set on the global object.
                //

                JavaScriptPropertyId hostPropertyId = JavaScriptPropertyId.FromString("host");

                //
                // Set the property.
                //

                globalObject.SetProperty(hostPropertyId, hostObject, true);

                //
                // Now create the host callbacks that we're going to expose to the script.
                //

                DefineHostCallback(hostObject, "echo", echoDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "runScript", runScriptDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "doSuccessfulWork", doSuccessfulWorkDelegate, IntPtr.Zero);
                DefineHostCallback(hostObject, "doUnsuccessfulWork", doUnsuccessfulWorkDelegate, IntPtr.Zero);

                //
                // Create an array for arguments.
                //

                JavaScriptValue hostArguments = JavaScriptValue.CreateArray((uint)(arguments.Length - argumentsStart));

                for (int index = argumentsStart; index < arguments.Length; index++)
                {
                    //
                    // Create the argument value.
                    //

                    JavaScriptValue argument = JavaScriptValue.FromString(arguments[index]);

                    //
                    // Create the index.
                    //

                    JavaScriptValue indexValue = JavaScriptValue.FromInt32(index - argumentsStart);

                    //
                    // Set the value.
                    //

                    hostArguments.SetIndexedProperty(indexValue, argument);
                }

                //
                // Get the name of the property that we're going to set on the host object.
                //

                JavaScriptPropertyId argumentsPropertyId = JavaScriptPropertyId.FromString("arguments");

                //
                // Set the arguments property.
                //

                hostObject.SetProperty(argumentsPropertyId, hostArguments, true);
            }

            return context;
        }

        private static void PrintScriptException(JavaScriptValue exception)
        {
            //
            // Get message.
            //

            JavaScriptPropertyId messageName = JavaScriptPropertyId.FromString("message");
            JavaScriptValue messageValue = exception.GetProperty(messageName);
            string message = messageValue.ToString();

            Console.Error.WriteLine("chakrahost: exception: {0}", message);
        }

        private static void PromiseContinuationCallback(JavaScriptValue task, IntPtr callbackState)
        {
            taskQueue.Enqueue(new JsTaskItem(task));
        }

        //
        // The main entry point for the host.
        //
        public static int Main(string[] arguments)
        {
            int returnValue = 1;
            CommandLineArguments commandLineArguments;
            commandLineArguments.ArgumentsStart = 0;
            var tasks = new List<Task>();

            if (arguments.Length - commandLineArguments.ArgumentsStart < 1)
            {
                Console.Error.WriteLine("usage: chakrahost <script name> <arguments>");
                return returnValue;
            }

            try
            {
                //
                // Create the runtime. We're only going to use one runtime for this host.
                //

                for (int i = 0; i < 10; i++)
                {
                    var task = Task.Run(() =>
                    {
                        using (JavaScriptRuntime runtime = JavaScriptRuntime.Create())
                        {
                            //
                            // Similarly, create a single execution context. Note that we're putting it on the stack here,
                            // so it will stay alive through the entire run.
                            //

                            JavaScriptContext context = CreateHostContext(runtime, arguments, commandLineArguments.ArgumentsStart);

                            //
                            // Now set the execution context as being the current one on this thread.
                            //

                            using (new JavaScriptContext.Scope(context))
                            {
                                JavaScriptRuntime.SetPromiseContinuationCallback(PromiseContinuationCallback, IntPtr.Zero);

                                //
                                // Load the script from the disk.
                                //

                                string script = File.ReadAllText(arguments[commandLineArguments.ArgumentsStart]);

                                //
                                // Run the script.
                                //

                                JavaScriptValue result;
                                try
                                {
                                    result = JavaScriptContext.RunScript(script, currentSourceContext++, arguments[commandLineArguments.ArgumentsStart]);

                                    // Call JS functions and continue the returned promises.
                                    /*JavaScriptPropertyId hostId = JavaScriptPropertyId.FromString("host");
                                    JavaScriptValue hostObject = JavaScriptValue.GlobalObject.GetProperty(hostId);

                                    JavaScriptPropertyId doSuccessfulJsWorkId = JavaScriptPropertyId.FromString("doSuccessfulJsWork");
                                    JavaScriptValue doSuccessfulJsWorkFunction = hostObject.GetProperty(doSuccessfulJsWorkId);
                                    JavaScriptValue resolvedPromise = doSuccessfulJsWorkFunction.CallFunction(JavaScriptValue.GlobalObject);

                                    ContinuePromise(resolvedPromise);

                                    JavaScriptPropertyId doUnsuccessfulJsWorkId = JavaScriptPropertyId.FromString("doUnsuccessfulJsWork");
                                    JavaScriptValue doUnsuccessfulJsWorkFunction = hostObject.GetProperty(doUnsuccessfulJsWorkId);
                                    JavaScriptValue rejectedPromise = doUnsuccessfulJsWorkFunction.CallFunction(JavaScriptValue.GlobalObject);

                                    ContinuePromise(rejectedPromise);*/

                                    // Pump messages in the task queue.
                                    PumpMessages();
                                }
                                catch (JavaScriptScriptException e)
                                {
                                    PrintScriptException(e.Error);
                                    return;
                                }
                                catch (Exception e)
                                {
                                    Console.Error.WriteLine("chakrahost: failed to run script: {0}", e.Message);
                                    return;
                                }

                                //
                                // Convert the return value.
                                //

                                JavaScriptValue numberResult = result.ConvertToNumber();
                                double doubleResult = numberResult.ToDouble();
                                returnValue = (int)doubleResult;
                            }
                        }
                    });

                    tasks.Add(task);
                }

                Task.WaitAll(tasks.ToArray());
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("chakrahost: fatal error: internal error: {0}.", e.Message);
            }

            Console.WriteLine(returnValue);
            Console.Read();
            return returnValue;
        }
    }
}
