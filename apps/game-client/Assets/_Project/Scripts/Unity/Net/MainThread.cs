using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace HappyWorld.Unity.Net
{
    /// <summary>Runs actions on Unity's main thread. Created once by GameSession.</summary>
    public sealed class MainThread : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static int _mainThreadId = -1;

        public static bool IsMainThread => System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        private void Awake()
        {
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        public static void Post(Action a)
        {
            if (IsMainThread) a();
            else Queue.Enqueue(a);
        }

        private void Update()
        {
            while (Queue.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
