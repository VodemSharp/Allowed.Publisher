using Allowed.Publisher.WindowsServices.Settings;
using Allowed.Publisher.WindowsServices.System;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Allowed.Publisher.WindowsServices.Publishers
{
    public class ServicePublisher
    {
        private readonly string _projectName;
        private readonly string _profile;

        private string projectFile = null;
        private string projectFolder = null;

        public ServicePublisher(string projectName, string profile)
        {
            _projectName = projectName;
            _profile = profile;
        }

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
                            Console.WriteLine($"{processName} {Path.GetRelativePath(projectFolder, localFile.FullName)}");

                            client.UploadFile(fileStream, remotePath + "/" + localFile.Name);
                        }
                    }
                }
            }
        }

        public async Task Publish()
        {
            Console.WriteLine("Publish started...");

            foreach (string file in Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.csproj", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(file);
                if (fileName == $"{_projectName}.csproj")
                {
                    projectFile = fileName;
                    projectFolder = Path.GetDirectoryName(file);
                }
            }

            if (string.IsNullOrEmpty(projectFile))
            {
                Console.WriteLine("The project file cannot be found!");
                return;
            }

            string profilePath = Path.IsPathRooted(_profile) ? _profile : Path.Combine(projectFolder, _profile);
            PublishSettings settings = JsonSerializer.Deserialize<PublishSettings>(await File.ReadAllTextAsync(profilePath));

            XmlSerializer serializer = new(typeof(PublisherProject));
            TextReader reader = new StringReader(await File.ReadAllTextAsync(Path.Combine(projectFolder, projectFile)));
            PublisherProject propertyGroup = (PublisherProject)serializer.Deserialize(reader);

            // Publish
            Process process = new();
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments = $"publish {Path.Combine(projectFolder, projectFile)} -c Release";
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

                if (command.ExitStatus != 0 && command.ExitStatus != 1062)
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

            Console.WriteLine("Publish Succeeded.");
        }
    }
}
