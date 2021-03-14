using Allowed.Publisher.WindowsServices.Settings;
using Allowed.Publisher.WindowsServices.System;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Text.Json;
using System.Xml.Serialization;

namespace Allowed.Publisher.WindowsServices
{
    [Cmdlet(VerbsData.Publish, "Service")]
    public class PublishServiceCommand : Cmdlet
    {
        [Parameter(Mandatory = true)]
        public string Profile { get; set; }

        private void UploadDirectory(SftpClient client, string localPath, string remotePath)
        {
            IEnumerable<FileSystemInfo> localFiles = new DirectoryInfo(localPath).EnumerateFileSystemInfos();
            IEnumerable<SftpFile> remoteFiles = client.ListDirectory(remotePath);

            foreach (FileSystemInfo localFile in localFiles)
            {
                if (localFile.Attributes.HasFlag(FileAttributes.Directory))
                {
                    string subPath = remotePath + "/" + localFile.Name;
                    if (!client.Exists(subPath))
                        client.CreateDirectory(subPath);

                    UploadDirectory(client, localFile.FullName, remotePath + "/" + localFile.Name);
                }
                else
                {
                    SftpFile remoteFile = remoteFiles.FirstOrDefault(f => f.Name == localFile.Name);

                    using (Stream fileStream = new FileStream(localFile.FullName, FileMode.Open))
                    {
                        if (remoteFile == null || localFile.LastWriteTimeUtc > remoteFile.LastWriteTimeUtc)
                        {
                            string processName = remoteFile == null ? "Adding" : "Updating";
                            Console.WriteLine($"{processName} {localFile.FullName} ({(FileInfo)localFile:N0} bytes)");

                            client.UploadFile(fileStream, remotePath + "/" + localFile.Name);
                        }
                    }
                }
            }
        }

        protected override async void ProcessRecord()
        {
            // Settings
            AssemblyName name = Assembly.GetEntryAssembly().GetName();
            string[] temp = Path.GetDirectoryName(name.CodeBase).Split('\\');
            temp = temp.Skip(1).Take(temp.Length - 4).ToArray();

            string projectFolder = string.Join('\\', temp);
            string profile = Path.IsPathRooted(Profile) ? Profile : Path.Combine(projectFolder, Profile);
            PublishSettings settings = JsonSerializer.Deserialize<PublishSettings>(
                await File.ReadAllTextAsync(profile));

            XmlSerializer serializer = new(typeof(PublisherProject));
            TextReader reader = new StringReader(await File.ReadAllTextAsync(Path.Combine(projectFolder, $"{name.Name}.csproj")));
            PublisherProject propertyGroup = (PublisherProject)serializer.Deserialize(reader);

            // Publish
            Process process = new();
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments = $"publish {name.Name}.csproj -c Release";
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
                return;

            // Stop service
            using (var client = new SshClient(settings.Host, settings.Username, settings.Password))
            {
                client.Connect();

                string commandStr = $"sc stop \"{settings.ServiceName}\"";
                Console.WriteLine(commandStr);

                SshCommand command = client.RunCommand(commandStr);
                Console.WriteLine(command.Result);

                client.Disconnect();

                if (command.ExitStatus != 0)
                    return;
            }

            // Upload files
            using (SftpClient client = new(settings.Host, settings.Username, settings.Password))
            {
                client.Connect();

                UploadDirectory(client, Path.Combine(projectFolder, "bin", "Release",
                    propertyGroup.PropertyGroup.TargetFramework, "publish"), settings.ServerFolder);

                client.Disconnect();
            }

            // Start service
            using (var client = new SshClient(settings.Host, settings.Username, settings.Password))
            {
                client.Connect();

                string commandStr = $"sc start \"{settings.ServiceName}\"";
                Console.WriteLine(commandStr);

                SshCommand command = client.RunCommand(commandStr);
                Console.WriteLine(command.Result);

                client.Disconnect();

                if (command.ExitStatus != 0)
                    return;
            }

            WriteObject(settings.ServiceName);
        }
    }
}
