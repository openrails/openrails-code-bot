using Newtonsoft.Json;

namespace Open_Rails_Code_Bot.GitHub
{
	class GraphQuery
	{
		[JsonProperty("query")]
		public string Query;
	}
}
