/*
 * Copyright notice for use of WebViewObject:
 * 
 * Copyright (C) 2012 GREE, Inc.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty.  In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would be
 *    appreciated but is not required.
 * 2. Altered source versions must be plainly marked as such, and must not be
 *    misrepresented as being the original software.
 * 3. This notice may not be removed or altered from any source distribution.
 */

using System;
using System.Collections;
using UnityEngine;
using Unity.Jobs;
using System.Diagnostics;
using System.IO;

#if UNITY_2018_4_OR_NEWER
using UnityEngine.Networking;
#endif
using UnityEngine.UI;
using Debug = UnityEngine.Debug;


public class CroquetRunner : MonoBehaviour
{
    public bool waitForUserLaunch;
    public bool showWebview;

    private static string bridgeSourcePath; // croquet-bridge folder under StreamingAssets
    private static string appSourcePath; // app's own folder under StreamingAssets
    private static string nodeExecPath = ""; // provided by CroquetBridge
    
    struct CroquetNodeProcess : IJob
    {
        public int port;

        private void OutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                Debug.Log("Croquet: " + outLine.Data);
            }
        }

        public void Execute()
        {
            // start the child process
            Process croquetProcess = new Process();

            // redirect the output stream of the child process.
            croquetProcess.StartInfo.UseShellExecute = false;
            croquetProcess.StartInfo.RedirectStandardOutput = true;
            croquetProcess.StartInfo.RedirectStandardError = true;
            croquetProcess.StartInfo.CreateNoWindow = true;

            string nodeEntry = "node-main.js";
            
            croquetProcess.StartInfo.FileName = nodeExecPath;
            croquetProcess.StartInfo.Arguments = $"{nodeEntry} {port}";

            croquetProcess.OutputDataReceived += OutputHandler;
            croquetProcess.ErrorDataReceived += OutputHandler;
            croquetProcess.EnableRaisingEvents = true;

            croquetProcess.StartInfo.WorkingDirectory = appSourcePath;

            int exitCode = -1;

            try
            {
                croquetProcess.Start();
                croquetProcess.BeginOutputReadLine();
                croquetProcess.BeginErrorReadLine();

                //UnityEngine.Debug.Log("Process id: " + croquetProcess.Id.ToString());

                // do not wait for the child process to exit before
                // reading to the end of its redirected stream.
                // croquetProcess.WaitForExit();

                // read the output stream first and then wait.
                //output = croquetProcess.StandardOutput.ReadToEnd();
                croquetProcess.WaitForExit();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("Run error: " + e.ToString()); // or throw new Exception
            }
            finally
            {
                exitCode = croquetProcess.ExitCode;
                UnityEngine.Debug.Log("Croquet process exit code: " + exitCode.ToString());

                croquetProcess.Dispose();
                croquetProcess = null;
            }
        }
    }
    
    public IEnumerator StartCroquetConnection(int port, string appName, bool useNodeJS, string pathToNode)
    {
        bridgeSourcePath = Path.Combine(Application.streamingAssetsPath, "croquet-bridge");
        appSourcePath = Path.Combine(Application.streamingAssetsPath, appName);

#if UNITY_EDITOR
        CroquetBuilder.WaitUntilBuildComplete();
#endif

        // options for running Croquet code (given lack of webview support on Windows):
        //
        //   editor on MacOS:
        //     a. webview
        //     b. user-launched browser
        //     c. nodeJS (using path from settings object)
        //     d. user-launched nodeJS
        //
        //   editor on Windows:
        //     e. nodeJS (using node.exe from package; forced when "user launched" is false)
        //     f. user-launched nodeJS
        //     g. user-launched browser
        //
        //   deployed standalone on anything other than Windows:
        //     h. webview
        //
        //   deployed standalone on Windows:
        //     i. nodeJS (using node.exe copied into StreamingAssets)
        
        // only compile with WebViewObject on non-Windows platforms
#if !(UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA)
        if (!useNodeJS && !waitForUserLaunch)
        {
            // cases (a), (h)
            WebViewObject webViewObject = (new GameObject("WebViewObject")).AddComponent<WebViewObject>();
            webViewObject.Init(
                separated: showWebview,
                enableWKWebView: true,

                cb: (msg) => { TimedLog(string.Format("CallFromJS[{0}]", msg)); },
                err: (msg) => { TimedLog(string.Format("CallOnError[{0}]", msg)); },
                httpErr: (msg) => { TimedLog(string.Format("CallOnHttpError[{0}]", msg)); },
                started: (msg) => { TimedLog(string.Format("CallOnStarted[{0}]", msg)); },
                hooked: (msg) => { TimedLog(string.Format("CallOnHooked[{0}]", msg)); },
                ld: (msg) =>
                {
                    TimedLog(string.Format("CallOnLoaded[{0}]", msg));
                    webViewObject.EvaluateJS(@"
                      if (window && window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.unityControl) {
                        window.Unity = {
                          call: function(msg) {
                            window.webkit.messageHandlers.unityControl.postMessage(msg);
                          }
                        }
                      } else {
                        window.Unity = {
                          call: function(msg) {
                            window.location = 'unity:' + msg;
                          }
                        }
                      }
                    ");
                    webViewObject.EvaluateJS(@"Unity.call('ua=' + navigator.userAgent)");
                }
            );
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            webViewObject.bitmapRefreshCycle = 1;
#endif

            //webViewObject.SetMargins(-5, -5, Screen.width - 8, Screen.height - 8);
            //webViewObject.SetMargins(5, 5, (int)(Screen.width * 0.6f), (int)(Screen.height * 0.6f));
            webViewObject.SetMargins(Screen.width - 5, Screen.height - 5, -100, -100);
            webViewObject.SetVisibility(showWebview);

            // webViewObject.SetTextZoom(100);  // android only. cf. https://stackoverflow.com/questions/21647641/android-webview-set-font-size-system-default/47017410#47017410

            // Use the port number determined by the bridge
            var webViewURL = $"http://localhost:{port}/{appName}/index.html";
            TimedLog("invoke LoadURL on " + webViewURL);

            webViewObject.LoadURL(webViewURL);
        }
#else // running in Windows
        if (!waitForUserLaunch) useNodeJS = true; // force node unless user explicitly wants an external browser
#endif

        if (!useNodeJS && waitForUserLaunch)
        {
            // cases (b), (g)
            TimedLog($"ready for browser to load from http://localhost:{port}/{appName}/index.html");
        }
        
        if (useNodeJS)
        {
            if (!waitForUserLaunch)
            {
                // cases (c), (e), (i)
                nodeExecPath = pathToNode;
                
                var job = new CroquetNodeProcess()
                {
                    port = port
                };
                JobHandle jobHandle = job.Schedule();
            }
            else
            {
                // cases (d), (f)
                TimedLog($"ready to run 'node node-main.js {port}' in {appSourcePath}");
            }
        }

        yield break;
    }

    void TimedLog(string msg)
    {
        UnityEngine.Debug.Log($"{System.DateTimeOffset.Now.ToUnixTimeMilliseconds() % 100000}: {msg}");
    }

}
