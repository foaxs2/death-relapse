using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace DeathRelapse.Editor
{
    [InitializeOnLoad]
    public static class UnityMcpBridge
    {
        private const int PORT = 8090;
        private static HttpListener listener;
        private static readonly List<string> consoleLogs = new List<string>();

        // Cache trên main thread để background thread đọc an toàn, không bị UnityException
        private static volatile string cachedUnityVersion = "";
        private static volatile bool cachedIsPlaying = false;
        private static volatile bool cachedIsPaused = false;
        private static volatile bool cachedIsCompiling = false;
        private static volatile string cachedSceneName = "";
        private static string cachedHierarchyJson = "{\"objects\":[]}";
        private static readonly object hierarchyLock = new object();

        static UnityMcpBridge()
        {
            Application.logMessageReceived += OnLogMessageReceived;
            EditorApplication.update += OnEditorUpdate;
            StartServer();
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            EditorApplication.quitting += StopServer;
        }

        private static void OnEditorUpdate()
        {
            cachedUnityVersion = Application.unityVersion;
            cachedIsPlaying = EditorApplication.isPlaying;
            cachedIsPaused = EditorApplication.isPaused;
            cachedIsCompiling = EditorApplication.isCompiling;
            try
            {
                var scene = EditorSceneManager.GetActiveScene();
                cachedSceneName = scene.name;

                // Cập nhật hierarchy nhẹ nhàng
                var rootObjs = scene.GetRootGameObjects();
                var list = new List<string>();
                foreach (var obj in rootObjs)
                {
                    if (obj != null)
                    {
                        list.Add($"{{\"name\":\"{obj.name}\",\"active\":{obj.activeSelf.ToString().ToLower()},\"pos\":{{\"x\":{obj.transform.position.x},\"y\":{obj.transform.position.y}}}}}");
                    }
                }
                lock (hierarchyLock)
                {
                    cachedHierarchyJson = $"{{\"objects\":[{string.Join(",", list)}]}}";
                }
            }
            catch { }
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] [{type}] {condition}";
            lock (consoleLogs)
            {
                if (consoleLogs.Count > 100) consoleLogs.RemoveAt(0);
                consoleLogs.Add(entry);
            }
        }

        [MenuItem("DeathRelapse/Start MCP Bridge")]
        public static void StartServer()
        {
            if (listener != null && listener.IsListening) return;

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{PORT}/");
                listener.Start();
                listener.BeginGetContext(OnContextReceived, null);
                Debug.Log($"[Unity MCP Bridge] Listening on http://127.0.0.1:{PORT}/");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Unity MCP Bridge] Failed to start server: {ex.Message}");
            }
        }

        [MenuItem("DeathRelapse/Stop MCP Bridge")]
        public static void StopServer()
        {
            if (listener != null)
            {
                try
                {
                    listener.Stop();
                    listener.Close();
                }
                catch { }
                listener = null;
            }
        }

        private static void OnContextReceived(IAsyncResult ar)
        {
            if (listener == null || !listener.IsListening) return;

            HttpListenerContext context;
            try
            {
                context = listener.EndGetContext(ar);
                listener.BeginGetContext(OnContextReceived, null);
            }
            catch
            {
                return;
            }

            HttpListenerRequest req = context.Request;
            HttpListenerResponse res = context.Response;

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string responseJson = "{}";
            int statusCode = 200;

            try
            {
                if (path == "/status")
                {
                    responseJson = $"{{\"unityVersion\":\"{cachedUnityVersion}\",\"isPlaying\":{cachedIsPlaying.ToString().ToLower()},\"isPaused\":{cachedIsPaused.ToString().ToLower()},\"isCompiling\":{cachedIsCompiling.ToString().ToLower()},\"scene\":\"{cachedSceneName}\"}}";
                }
                else if (path == "/console")
                {
                    lock (consoleLogs)
                    {
                        var escaped = new List<string>();
                        foreach (var log in consoleLogs)
                        {
                            escaped.Add($"\"{log.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "")}\"");
                        }
                        responseJson = $"{{\"logs\":[{string.Join(",", escaped)}]}}";
                    }
                }
                else if (path == "/play")
                {
                    EditorApplication.delayCall += () =>
                    {
                        EditorApplication.isPlaying = !EditorApplication.isPlaying;
                    };
                    responseJson = "{\"status\":\"playmode_toggled\"}";
                }
                else if (path == "/setup_demo")
                {
                    responseJson = "{\"status\":\"setup_demo_not_implemented\"}";
                }
                else if (path == "/hierarchy")
                {
                    lock (hierarchyLock)
                    {
                        responseJson = cachedHierarchyJson;
                    }
                }
                else
                {
                    statusCode = 404;
                    responseJson = "{\"error\":\"not_found\"}";
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                responseJson = $"{{\"error\":\"{ex.Message}\"}}";
            }

            byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
            res.StatusCode = statusCode;
            res.ContentType = "application/json";
            res.ContentLength64 = buffer.Length;
            res.OutputStream.Write(buffer, 0, buffer.Length);
            res.OutputStream.Close();
        }
    }
}