﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Dynamo.TypeScriptCompiler
{
	public class TypeScriptCompiler : ITypeScriptCompiler
	{
        // Fields
		private readonly int _timeout;
		private readonly String _executablePath;

		// Constructors
		public TypeScriptCompiler(TypeScriptCompilerOptions options = null, int timeout = 10000, ITypeScriptExecutableResolver tsExecResolver = null)
		{
		    Options = options ?? new TypeScriptCompilerOptions();
			_timeout = timeout;

			if (tsExecResolver == null)
				tsExecResolver = new TypeScriptExecutableVersionResolver();

            _executablePath = tsExecResolver.GetExecutablePath();	// Should it look up the compiler on every compile instead?
		}

        // Properties
	    public TypeScriptCompilerOptions Options { get; private set; }

        // Methods
		public ITypeScriptCompilerResult Compile(string filePath)
		{
			if (filePath == null)
				throw new ArgumentNullException("filePath");

			if (!File.Exists(filePath))
				throw new ArgumentException("File does not exist", "filePath");

			var fileName = Path.GetFileName(filePath);
		    var outputSourceFileName = Path.ChangeExtension(fileName, ".js");
			var outputFolder = Options.SaveToDisk ? Path.GetDirectoryName(filePath) : Path.GetTempPath().TrimEnd(new[] { '\\' });
			
			if (Options.SourceMap && !Options.SaveToDisk)
			{
				// Files need to be saved to the same folder when a sourcemap is genereated (because of the reference to the source)
				// Easiest way to solve it is to output to either the folder of the target file or copy the target file to the temp folder

				// TODO: 0.9.5 have sourceRoot arg - so this doesnt matter anymore ?

				var newFilePath = Path.Combine(outputFolder, fileName);
				File.Copy(filePath, newFilePath);
				filePath = newFilePath;
			}

			String outputSourcePath = Path.Combine(outputFolder, outputSourceFileName);
			String outputSourceMapPath = outputSourcePath + ".map";

			var args = GetArgs(filePath, outputFolder);

			var processStartInfo = new ProcessStartInfo
			{
				WindowStyle = ProcessWindowStyle.Hidden,
				CreateNoWindow = true,
				Arguments = args,
				FileName = _executablePath,
				UseShellExecute = false,
				RedirectStandardError = true
			};
			processStartInfo.EnvironmentVariables.Add("file", filePath);

            using (var process = new Process() { StartInfo = processStartInfo })
		    {
				// Start
                process.Start();

				// Wait until compiling has finished
			    if (!process.WaitForExit(_timeout) || process.ExitCode != 0)
			    {
					// Time-out or ExitCode not 0
					return new TypeScriptCompilerResult(process.ExitCode, error: process.StandardError.ReadToEnd());
			    }

			    if (Options.SaveToDisk)
			    {				    
					var sourceFactory = new Func<String>(() => File.ReadAllText(outputSourcePath));
				    Func<String> sourceMapFactory = null;

				    if (Options.SourceMap)
						sourceMapFactory = () => File.ReadAllText(outputSourceMapPath);
				
				    return new TypeScriptCompilerResult(process.ExitCode, sourceFactory, sourceMapFactory);
			    }
			    else
			    {
					// Read temporary file
				    var source = File.ReadAllText(outputSourcePath);

					// Delete temporary file (else it wouldnt be temporary)
				    File.Delete(outputSourcePath);

				    String sourceMap = null;

				    if (Options.SourceMap)
				    {
						sourceMap = File.ReadAllText(outputSourceMapPath);
						File.Delete(outputSourceMapPath);
						// Also delete the filePath as it is temporary - needed to fix the reference in the sourcemap
					    File.Delete(filePath);
				    }

				    return new TypeScriptCompilerResult(process.ExitCode, source, sourceMap);
			    }
		    }
		}

		private String GetArgs(String filePath, String outputFolder)
		{
			var args = "\"" + filePath + "\" --outDir \"" + outputFolder + "\" --target " + Options.Target;

			args += CreateArgIfTrue(Options.Declaration, "-d");
			args += CreateArgIfTrue(Options.MapRoot != null, "--mapRoot " + Options.MapRoot);
			args += CreateArgIfTrue(Options.NoImplicitAny, "--noImplicitAny");
			args += CreateArgIfTrue(Options.NoResolve, "--noResolve");						// TODO: Removed in version 1.0
			args += CreateArgIfTrue(Options.RemoveComments, "--removeComments");
			args += CreateArgIfTrue(Options.SourceMap, "--sourceMap");
			args += CreateArgIfTrue(Options.SourceRoot != null, "--sourceRoot " + Options.SourceRoot);

			return args;
		}

		private static String CreateArgIfTrue(Boolean option, String arg)
		{
			if (option)
				return " " + arg;
			return "";
		}
	}
}
