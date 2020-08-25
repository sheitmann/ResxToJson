// ******************************************************************************
//  Copyright (C) CROC Inc. 2014. All rights reserved.
// ******************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Croc.DevTools.ResxToJson
{

	public class ResxToJsonConverter
	{
		class JsonResources
		{
			public JsonResources()
			{
				LocalizedResources = new Dictionary<CultureInfo, JObject>();
			}

			public JObject BaseResources { get; set; }

			public IDictionary<CultureInfo, JObject> LocalizedResources { get; }
		}

		public static ConverterLogger Convert(ResxToJsonConverterOptions options)
		{
			var logger = new ConverterLogger();

			var formatter = GetFormatter(options.OutputFormat);
			logger.AddMsg(Severity.Info, $"Formatter {formatter.GetType().Name} is used.");
			if (!formatter.CheckOptions(options, out string checkOptionsMessage)) {
				logger.AddMsg(Severity.Error, checkOptionsMessage);
				return logger;
			}

			IDictionary<string, ResourceBundle> bundles = null;
			if (options.InputFiles.Count > 0)
			{
				bundles = ResxHelper.GetResources(options.InputFiles, logger);
			}
			if (options.InputFolders.Count > 0)
			{
				var bundles2 = ResxHelper.GetResources(options.InputFolders, options.Recursive, logger);
				if (bundles == null )
				{
					bundles = bundles2;
				}
				else
				{
					// join two bundles collection
					foreach (var pair in bundles2)
					{
						bundles[pair.Key] = pair.Value;
					}
				}
			}

			if (bundles == null || bundles.Count == 0)
			{
				logger.AddMsg(Severity.Warning, "No resx files were found");
				return logger;
			}
			logger.AddMsg(Severity.Trace, "Found {0} resx bundles", bundles.Count);
			
			if (bundles.Count > 1 && !String.IsNullOrEmpty(options.OutputFile))
			{
				// join multiple resx resources into a single js-bundle
				var bundleMerge = new ResourceBundle(Path.GetFileNameWithoutExtension(options.OutputFile));
				foreach (var pair in bundles)
				{
					bundleMerge.MergeWith(pair.Value);
				}
				logger.AddMsg(Severity.Trace, "As 'outputFile' option was specified all bundles were merged into single bundle '{0}'", bundleMerge.BaseName);
				bundles = new Dictionary<string, ResourceBundle> {{bundleMerge.BaseName, bundleMerge}};
			}

			foreach (ResourceBundle bundle in bundles.Values)
			{
				JsonResources jsonResources = generateJsonResources(formatter, bundle, options);
				string baseFileName;
				string baseDir;
				if (!string.IsNullOrEmpty(options.OutputFile))
				{
					baseFileName = Path.GetFileName(options.OutputFile);
					baseDir = Path.GetDirectoryName(options.OutputFile);
				}
				else
				{
					baseFileName = bundle.BaseName.ToLowerInvariant() + formatter.OutputFileExtension;
					baseDir = options.OutputFolder;
				}
				if (string.IsNullOrEmpty(baseDir))
				{
					baseDir = Environment.CurrentDirectory;
				}

				logger.AddMsg(Severity.Trace, "Processing '{0}' bundle (contains {1} resx files)", bundle.BaseName,
					bundle.Cultures.Count);
				string dirPath = formatter.GetBaseOutputDirectory(baseDir, CultureInfo.InvariantCulture, options);
                string outputPath = Path.Combine(dirPath, formatter.GetLanguageFileName(baseFileName, CultureInfo.InvariantCulture, options));
				string jsonText = stringifyJson(formatter, jsonResources.BaseResources, options);
				writeOutput(outputPath, jsonText, options, logger);

				if (jsonResources.LocalizedResources.Count > 0)
				{
					foreach (KeyValuePair<CultureInfo, JObject> pair in jsonResources.LocalizedResources) {
						dirPath = formatter.GetBaseOutputDirectory(baseDir, pair.Key, options);
						outputPath = Path.Combine(dirPath, formatter.GetLanguageFileName(baseFileName, pair.Key, options));
						jsonText = stringifyJson(formatter, pair.Value, options);
						writeOutput(outputPath, jsonText, options, logger);
					}
				}
			}

			return logger;
		}

		private static IResxToJsonFormatter GetFormatter(OutputFormat outputFormat) {
			switch (outputFormat) {
				case OutputFormat.RequireJs:
					return new RequireJsFormatter();
					
				case OutputFormat.i18next:
					return new i18nextFormatter();
					
				case OutputFormat.DevExtreme:
					return new DevExtremeFormatter();
					
				default:
					throw new ArgumentOutOfRangeException(nameof(outputFormat), outputFormat, null);
			}
		}

		private static void writeOutput(string outputPath, string jsonText, ResxToJsonConverterOptions options, ConverterLogger logger)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
			if (File.Exists(outputPath))
			{
				var attrs = File.GetAttributes(outputPath);
				if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				{
					if (options.Overwrite == OverwriteModes.Skip)
					{
						logger.AddMsg(Severity.Error, "Cannot overwrite {0} file, skipping", outputPath);
						return;
					}
					// remove read-only attribute
					attrs = ~FileAttributes.ReadOnly & attrs;
					File.SetAttributes(outputPath, attrs);
				}
				// if existing file isn't readonly we just overwrite it
			}
			File.WriteAllText(outputPath, jsonText, Encoding.UTF8);
			logger.AddMsg(Severity.Info, "Created {0} file", outputPath);
		}

		static string stringifyJson(IResxToJsonFormatter formatter, JObject json, ResxToJsonConverterOptions options) {
			return formatter.GetFileContent(json, options);
		}

		private static JsonResources generateJsonResources(IResxToJsonFormatter formatter, ResourceBundle bundle,
			ResxToJsonConverterOptions options)
		{
			var result = new JsonResources();
			// root resoruce
			IDictionary<string, string> baseValues = bundle.GetValues(null);
			JObject jBaseValues = convertValues(baseValues, options);
			result.BaseResources = formatter.GetJsonResource(jBaseValues, CultureInfo.InvariantCulture, bundle, options);
		    
			// culture specific resources
			foreach (CultureInfo culture in bundle.Cultures)
			{
				if (culture.Equals(CultureInfo.InvariantCulture)) {
					continue;
				}

				IDictionary<string, string> values = bundle.GetValues(culture);
				if (options.UseFallbackForMissingTranslation) {
					foreach (var baseValue in baseValues) {
						if (!values.ContainsKey(baseValue.Key)) {
							values[baseValue.Key] = baseValues[baseValue.Key];
						}
					}
				}
				JObject jCultureValues = convertValues(values, options);
				result.LocalizedResources[culture] = formatter.GetJsonResource(jCultureValues, culture, bundle, options);
			}
			return result;
		}

		private static JObject convertValues(IDictionary<string, string> values, ResxToJsonConverterOptions options)
		{
			var json = new JObject();
			foreach (KeyValuePair<string, string> pair in values)
			{
				string fieldName = pair.Key;
				switch (options.Casing)
				{
					case JsonCasing.Camel:
						char[] chars = fieldName.ToCharArray();
						chars[0] = Char.ToLower(chars[0]);
						fieldName = new string(chars);
						break;
					case JsonCasing.Lower:
						fieldName = fieldName.ToLowerInvariant();
						break;
				}
				json[fieldName] = pair.Value;
			}
			return json;
		}
	}
}