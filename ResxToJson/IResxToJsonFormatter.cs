using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Croc.DevTools.ResxToJson {
	public interface IResxToJsonFormatter {

		string OutputFileExtension { get; }

		string GetBaseOutputDirectory(string baseOutputDirectory, CultureInfo cultureInfo, ResxToJsonConverterOptions options);

		string GetLanguageFileName(string baseFileName, CultureInfo cultureInfo, ResxToJsonConverterOptions options);

		JObject GetJsonResource(JObject jBaseValues, CultureInfo cultureInfo, ResourceBundle bundle, ResxToJsonConverterOptions options);

		string GetFileContent(JObject json, ResxToJsonConverterOptions options);

		bool CheckOptions(ResxToJsonConverterOptions options, out string message);
	}

	public abstract class ResxToJsonFormatter : IResxToJsonFormatter {
		#region Implementation of IResxToJsonFormatter

		public virtual string OutputFileExtension => ".json";

		public virtual string GetBaseOutputDirectory(string baseOutputDirectory, CultureInfo cultureInfo, ResxToJsonConverterOptions options) {
			if (Equals(cultureInfo, CultureInfo.InvariantCulture)) {
				return baseOutputDirectory;
			}
			return Path.Combine(baseOutputDirectory, cultureInfo.Name);
		}

		public virtual string GetLanguageFileName(string baseFileName, CultureInfo cultureInfo, ResxToJsonConverterOptions options) {
			return baseFileName;
		}

		public virtual JObject GetJsonResource(JObject jBaseValues, CultureInfo cultureInfo, ResourceBundle bundle, ResxToJsonConverterOptions options) {
			return jBaseValues;
		}

		public virtual string GetFileContent(JObject json, ResxToJsonConverterOptions options) {
			return json.ToString(Formatting.Indented);
		}

		public virtual bool CheckOptions(ResxToJsonConverterOptions options, out string message) {
			message = string.Empty;
			return true;
		}

		#endregion
	}

	public class RequireJsFormatter : ResxToJsonFormatter {
		#region Implementation of IResxToJsonFormatter

		public override string OutputFileExtension => ".js";

		public override JObject GetJsonResource(JObject jBaseValues, CultureInfo cultureInfo, ResourceBundle bundle, ResxToJsonConverterOptions options) {
			if (!Equals(cultureInfo, CultureInfo.InvariantCulture)) {
				return jBaseValues;
			}
			// When dealing with require.js i18n the root resource contains a "root" subnode that contains all 
			// of the base translations and then a bunch of nodes like the following for each supported culture:
			//   "en-US" : true
			//   "fr" : true
			//   ...
			var jRoot = new JObject();
			jRoot["root"] = jBaseValues;
			foreach (CultureInfo bundleCulture in bundle.Cultures) {
				if (bundleCulture.Equals(CultureInfo.InvariantCulture)) {
					continue;
				}
				jRoot[cultureInfo.Name] = true;
			}
			return jRoot;
		}

		public override string GetFileContent(JObject json, ResxToJsonConverterOptions options) {
			return "define(" + base.GetFileContent(json, options) + ");";
		}

		#endregion
	}

	public class i18nextFormatter : ResxToJsonFormatter {

		public override string GetBaseOutputDirectory(string baseOutputDirectory, CultureInfo cultureInfo, ResxToJsonConverterOptions options) {
			if (Equals(cultureInfo, CultureInfo.InvariantCulture)) {
				return Path.Combine(baseOutputDirectory, options.FallbackCulture);
			}

			return base.GetBaseOutputDirectory(baseOutputDirectory, cultureInfo, options);
		}
	}

	public class DevExtremeFormatter : ResxToJsonFormatter {

		public override string OutputFileExtension => ".js";

		public override string GetBaseOutputDirectory(string baseOutputDirectory, CultureInfo cultureInfo, ResxToJsonConverterOptions options) {
			return baseOutputDirectory;
		}

		#region Overrides of ResxToJsonFormatter

		public override string GetLanguageFileName(string baseFileName, CultureInfo cultureInfo, ResxToJsonConverterOptions options) {
			string cultureName = cultureInfo.Name;
			if (Equals(cultureInfo, CultureInfo.InvariantCulture)) {
				cultureName = options.FallbackCulture;
			}
			
			string extension = Path.GetExtension(baseFileName);
			baseFileName = Path.ChangeExtension(baseFileName, cultureName + extension);
			return baseFileName;
		}

		#endregion

		public override JObject GetJsonResource(JObject jBaseValues, CultureInfo cultureInfo, ResourceBundle bundle, ResxToJsonConverterOptions options) {
			string cultureName = cultureInfo.Name;
			if (Equals(cultureInfo, CultureInfo.InvariantCulture)) {
				cultureName = options.FallbackCulture;
			}

			var jRoot = new JObject();
			jRoot[cultureName] = jBaseValues;

			return jRoot;
		}

		public override string GetFileContent(JObject json, ResxToJsonConverterOptions options) {
			string jsonSerialized = SerializeJsonObject(json);

			string content = $@"""use strict"";

! function(root, factory) {{
	if (""function"" === typeof define && define.amd) {{
		define(function(require) {{
			factory(require(""devextreme/localization""));
		}})
	}} else {{
		if (""object"" === typeof module && module.exports) {{
			factory(require(""devextreme/localization""));
		}} else {{
			factory(DevExpress.localization);
		}}
	}}
}} (this, function(localization) {{
	localization.loadMessages({jsonSerialized});
}}); ";
			return content;
		}

		public override bool CheckOptions(ResxToJsonConverterOptions options, out string message) {
			if (string.IsNullOrEmpty(options.FallbackCulture)) {
				message = "The parameter fallbackCulture is not specified.";
				return false;
			}

			message = string.Empty;
			return true;
		}

		private static string SerializeJsonObject(JObject json) {
			JsonSerializer serializer = new JsonSerializer();
			serializer.StringEscapeHandling = StringEscapeHandling.EscapeNonAscii;
			
			using (StringWriter stringWriter = new StringWriter(CultureInfo.InvariantCulture)) {
				JsonTextWriter jsonTextWriter = new JsonTextWriter(stringWriter);
				jsonTextWriter.Formatting = Formatting.Indented;
				jsonTextWriter.IndentChar = '\t';
				jsonTextWriter.Indentation = 1;
				serializer.Serialize(jsonTextWriter, json);
				return stringWriter.ToString();
			}
		}
	}
}