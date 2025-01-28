using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Xml;
using MessagePack;
using ZstdSharp;

namespace i18nEx.ZstdMsgPackPacker
{
	internal class Program
	{
		private static readonly MessagePackSerializerOptions SerializerOptions = new(MessagePack.Resolvers.ContractlessStandardResolver.Instance);

		private static async Task<int> Main(string[] args)
		{
			using (var p = Process.GetCurrentProcess())
				p.PriorityClass = ProcessPriorityClass.BelowNormal;

			var rootCommand = new RootCommand("A simple CLI tool to un/pack msgpack files for usage with the ZstdMsgPackLoader");

			{
				var packCommand = new Command(
					name: "pack",
					description: "Packs a directory into a single MsgPack file.");

				rootCommand.AddCommand(packCommand);

				var directoryOption =
					new Option<DirectoryInfo>(aliases: ["--directory", "-d"],
						description: "Directory to be packed.")
					{
						IsRequired = true
					};

				var doCompressionOption =
					new Option<bool>(
						aliases: ["--compress", "-c"],
						description: "Compress the MsgPack to a zst file.");

				var outputPathOption =
					new Option<string>(aliases: ["--output", "-o"],
						"The full path to output the file, including the file name.")
					{
						IsRequired = true
					};

				packCommand.AddOption(directoryOption);
				packCommand.AddOption(doCompressionOption);
				packCommand.AddOption(outputPathOption);

				packCommand.SetHandler(
					PackFile,
					directoryOption, outputPathOption, doCompressionOption);
			}
			{
				var unpackCommand = new Command(
					name: "unpack",
					description: "Unpacks a MsgPack/Zst file into a directory.");

				rootCommand.AddCommand(unpackCommand);

				var fileOption =
					new Option<FileInfo>(aliases: ["--file", "-f"],
						description: "MsgPack file to be unpacked.")
					{
						IsRequired = true
					};

				var unpackDirectory =
					new Option<DirectoryInfo>(aliases: ["--output", "-o"],
						description: "Directory to place the unpacked files in.")
					{
						IsRequired = true
					};

				unpackCommand.AddOption(fileOption);
				unpackCommand.AddOption(unpackDirectory);

				unpackCommand.SetHandler(
					UnpackFile,
					fileOption, unpackDirectory);
			}

			return await rootCommand.InvokeAsync(args);
		}

		private static void UnpackFile(FileInfo inputFile, DirectoryInfo outputDirectory)
		{
			/*
			using var bsonFileStream = inputFile.OpenRead();
			using var bsonReader = new BsonDataReader(bsonFileStream);
			var serializer = new JsonSerializer();
			var deserializeJsonFile = serializer.Deserialize<Dictionary<string, byte[]>>(bsonReader);
			*/

			var data = File.ReadAllBytes(inputFile.FullName);

			if (inputFile.FullName.EndsWith(".zst"))
			{
				using var compressor = new ZstdSharp.Decompressor();
				data = compressor
					.Unwrap(data)
					.ToArray();
			}

			var deserializedFile = MessagePackSerializer.Deserialize<Dictionary<string, byte[]>>(data, SerializerOptions);

			if (deserializedFile == null)
			{
				return;
			}

			var counter = 0;
			foreach (var file in deserializedFile)
			{
				Console.WriteLine($"{++counter}/{deserializedFile.Count} : {file.Key}...");
				var filePath = Path.Combine(outputDirectory.FullName, Path.GetFileNameWithoutExtension(inputFile.Name), file.Key);
				Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException());
				File.WriteAllBytes(filePath, file.Value);
			}
		}

		private static void PackFile(DirectoryInfo directory, string outputName, bool compression = false)
		{
			var filesDictionary = new Dictionary<string, byte[]>();
			var filesInDir = directory.EnumerateFiles("*", SearchOption.AllDirectories)
				.ToArray();

			var counter = 0;
			foreach (var file in filesInDir)
			{
				var relativeName = Path.GetRelativePath(directory.FullName, file.FullName);
				var textContent = File.ReadAllText(file.FullName).Trim();

				Console.WriteLine($"Reading {++counter}/{filesInDir.Length}: {relativeName}...");

				if (filesDictionary.ContainsKey(relativeName))
				{
					Console.WriteLine($"{relativeName} was already declared!! Skipping...");
					continue;
				}

				filesDictionary.Add(relativeName, Encoding.UTF8.GetBytes(textContent));
			}

			var fileName = directory.Name;
			var backupOutput = Path.Combine(directory.Parent?.FullName ?? string.Empty, fileName);

			var outputPath = string.IsNullOrEmpty(outputName) ? backupOutput : outputName;

			using var outputStream = File.OpenWrite(outputPath);

			if (compression)
			{
				using var compressor = new CompressionStream(outputStream);
				MessagePackSerializer.Serialize(compressor, filesDictionary, SerializerOptions);
			}
			else
			{
				MessagePackSerializer.Serialize(outputStream, filesDictionary, SerializerOptions);
			}

			Console.WriteLine($"Done, saved file to {outputPath}");
		}
	}
}