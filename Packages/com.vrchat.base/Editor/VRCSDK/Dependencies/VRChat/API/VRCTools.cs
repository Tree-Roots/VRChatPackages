﻿using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("VRC.SDK3A.Editor")]
[assembly: InternalsVisibleTo("VRC.SDK3.Editor")]
namespace VRC.SDKBase.Editor.Api
{
    internal class VRCTools
    {
        internal static byte[] GetFileMD5(string filePath)
        {
            var hash = MD5.Create();
            return hash.ComputeHash(File.OpenRead(filePath));
        }

        internal static async Task<string> GenerateFileSignature(string sourceFilePath, string targetFilePath)
        {
            var inStream = librsync.net.Librsync.ComputeSignature(File.OpenRead(sourceFilePath));
            var outStream = File.Open(targetFilePath, FileMode.Create, FileAccess.Write);
            await inStream.CopyToAsync(outStream);
            inStream.Close();
            outStream.Close();
            return targetFilePath;
        }
        
        internal static async Task CleanupTempFiles(string fileId)
        {
            var folder = VRC.Tools.GetTempFolderPath(fileId);

            while (Directory.Exists(folder))
            {
                try
                {
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                }
                catch (System.Exception)
                {
                    // ignored
                }

                await Task.Delay(10);
            }
        }
        
        internal static string GetMimeTypeFromExtension(string extension)
        {
            if (extension == ".vrcw")
                return "application/x-world";
            if (extension == ".vrca")
                return "application/x-avatar";
            if (extension == ".dll")
                return "application/x-msdownload";
            if (extension == ".unitypackage")
                return "application/gzip";
            if (extension == ".gz")
                return "application/gzip";
            if (extension == ".jpg")
                return "image/jpg";
            if (extension == ".png")
                return "image/png";
            if (extension == ".sig")
                return "application/x-rsync-signature";
            if (extension == ".delta")
                return "application/x-rsync-delta";
            
            return "application/octet-stream";
        }
        
        /// <summary>
        /// This expands the send buffer size from around 256KB up to 4MB (our preferred send buffer size)
        /// This dramatically speeds up file uploads
        ///
        /// Unfortunately, APIs meant to be used for this aren't available in Mono version of .NET in Unity
        /// So we have to use reflection to get to the private fields and properties we need
        /// </summary>
        /// <param name="targetUrl">Url to expand the send buffer for</param>
        /// <param name="sendRequest">Request task to expand the buffer for</param>
        /// <param name="cancellationToken"></param>
        internal static async Task IncreaseSendBuffer(Uri targetUrl, Task sendRequest,
            CancellationToken cancellationToken)
        {
            var servicePointGroups = typeof(ServicePoint).GetField("groups", BindingFlags.NonPublic | BindingFlags.Instance);
            var groupsList = servicePointGroups?.FieldType.GetProperty("Values");
            
            var connectionStateType = servicePointGroups?.FieldType.GenericTypeArguments[1];
            var connectionsList = connectionStateType?.GetField("connections", BindingFlags.NonPublic | BindingFlags.Instance);

            var connectionListStateType = connectionsList?.FieldType.GenericTypeArguments[0];
            var connection = connectionListStateType?.GetProperty("Connection");
            var socket = connection?.PropertyType.GetField("socket", BindingFlags.NonPublic | BindingFlags.Instance);
            
            while (!sendRequest.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken);
                
                var servicePoint = ServicePointManager.FindServicePoint(targetUrl);
                var groups = (IEnumerable)groupsList?.GetValue(servicePointGroups.GetValue(servicePoint));

                // we're going to retry finding the active service point
                if (groups == null) continue;

                foreach (var group in groups)
                {
                    var connections = (IEnumerable) connectionsList?.GetValue(group);
                    if (connections == null) continue;
                    foreach (var connectionState in connections)
                    {
                        var webConnection = connection?.GetValue(connectionState);
                        if (webConnection == null) continue;
                        var socketInstance = (Socket) socket?.GetValue(webConnection);
                        if (socketInstance != null && socketInstance.Connected && socketInstance.SendBufferSize < 4 * 1024 * 1024)
                        {
                            socketInstance.SendBufferSize = 4 * 1024 * 1024;
                        }
                    }
                }
            }
        }
    }
}

