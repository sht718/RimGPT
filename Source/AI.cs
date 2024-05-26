using HarmonyLib;
using Newtonsoft.Json;
using OpenAI;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimGPT
{
	public class AI
	{
		public int modelSwitchCounter = 0;
		public static JsonSerializerSettings settings = new() { NullValueHandling = NullValueHandling.Ignore, MissingMemberHandling = MissingMemberHandling.Ignore };

#pragma warning disable CS0649
		public class Input
		{
			public string CurrentWindow { get; set; }
			public List<string> PreviousHistoricalKeyEvents { get; set; }
			public string LastSpokenText { get; set; }
			public List<string> ActivityFeed { get; set; }
			public string[] ColonyRoster { get; set; }
			public string ColonySetting { get; set; }
			public string ResearchSummary { get; set; }
			public string ResourceData { get; set; }
			public string EnergyStatus { get; set; }
			public string EnergySummary { get; set; }
			public string RoomsSummary { get; set; }
		}

		private float FrequencyPenalty { get; set; } = 0.5f;
		private readonly int maxRetries = 3;
		struct Output
		{
			public string ResponseText { get; set; }
			public string[] NewHistoricalKeyEvents { get; set; }
		}
#pragma warning restore CS0649

		// OpenAIApi is now a static object, the ApiConfig details are added by ReloadGPTModels.
		//public OpenAIApi OpenAI => new(RimGPTMod.Settings.chatGPTKey);
		private List<string> history = [];

		public const string defaultPersonality = "你是一名正在观看玩家玩流行游戏《Rimworld》的评论员。";

		public string SystemPrompt(Persona currentPersona)
		{
			var playerName = Tools.PlayerName();
			var player = playerName == null ? "玩家" : $"玩家是'{playerName}'";
			var otherObservers = RimGPTMod.Settings.personas.Where(p => p != currentPersona).Join(persona => $"'{persona.name}'");
			var exampleInput = JsonConvert.SerializeObject(new Input
			{
				CurrentWindow = "<当前窗口信息>",
				ActivityFeed = ["事件1", "事件2", "事件3"],
				PreviousHistoricalKeyEvents = ["历史事件1", "历史事件2", "历史事件3"],
				LastSpokenText = "<之前响应的文本, 最近一次你说的话>",
				ColonyRoster = ["游戏角色1", "游戏角色2", "游戏角色3"],
				ColonySetting = "<游戏殖民地的设定和环境描述>",
				ResourceData = "<一些资源的定期更新报告>",
				RoomsSummary = "<一份定期更新的殖民地房间信息的报告，若玩家禁用了一项设置，它可能就永远不会更新>",
				ResearchSummary = "<一份定期更新的研究内容的报告，若玩家禁用了一项设置，它可能就永远不会更新，内容包括已经研究过的、当前正在研究的以及可供研究的东西>",
				EnergySummary = "<一份定期更新的殖民地发电和消耗需求的报告，如果玩家禁用了一项设置，它可能就永远不会更新>"

			}, settings);
			var exampleOutput = JsonConvert.SerializeObject(new Output
			{
				ResponseText = "<最新解说>",
				NewHistoricalKeyEvents = ["历史事件摘要", "事件1和事件2", "事件3"]
			}, settings);


			return new List<string>
				{
						$"You are {currentPersona.name}.\n",
						// Adds weight to using its the personality with its responses: as a chronicler, focusing on balanced storytelling, or as an interactor, focusing on personality-driven improvisation.						
						currentPersona.isChronicler ? "除非另有说明, 请平衡重大事件和微妙细节，并以你独特的风格来表达它们。"
																				: "除非另有说明, 互动时请体现出你独特的个性，采用一种基于你的背景、当前状况以及他人行为的即兴方式。",
						$"除非另有说明, ", otherObservers.Any() ? $"你的其他观察者同伴们是{otherObservers}. " : "",
						$"除非另有说明, ",(otherObservers.Any() ? $"你们都在观看" : "你在观看") + $"'{player}'玩Rimworld游戏.\n",
						$"你的角色/个性是: {currentPersona.personality.Replace("PLAYERNAME", player)}\n",
						$"你的输入来自当前游戏，并且一定会以这样的JSON格式输入: {exampleInput}\n",
						$"你的输出必须严格遵守这样的JSON格式: {exampleOutput}\n",
						$"Limit ResponseText to no more than {currentPersona.phraseMaxWordCount} words.\n",
						$"Limit NewHistoricalKeyEvents to no more than {currentPersona.historyMaxWordCount} words.\n",

						// Encourages the AI to consider how its responses would sound when spoken, ensuring clarity and accessibility.
						// $"When constructing the 'ResponseText', consider vocal clarity and pacing so that it is easily understandable when spoken by Microsoft Azure Speech Services.\n",

						// Prioritizes sources of information.
						$"更新优先级：1. ActivityFeed，2. 额外的信息（作为简要背景）.\n",
						// Further reinforces the AI's specific personality by resynthesizing different pieces of information and storing it in its own history
						$"结合之前的PreviousHistoricalKeyEvents，以及来自“ActivityFeed”的每个事件，并将其合成为一种新的、简洁的“NewHistoricalKeyEvents”形式，确保新的合成符合你的角色设定。\n",
						// Guides the AI in understanding the sequence of events, emphasizing the need for coherent and logical responses or interactions.
						"Items sequence in 'LastSpokenText', 'PreviousHistoricalKeyEvents', and 'ActivityFeed' reflects the event timeline; use it to form coherent responses or interactions.\n",
						$"Remember: your output MUST be valid JSON and 'NewHistoricalKeyEvents' MUST ONLY contain simple text entries, each encapsulated in quotes as string literals.\n",
						$"For example, {exampleOutput}. No nested objects, arrays, or non-string data types are allowed within 'NewHistoricalKeyEvents'.\n",
						"最后，ResponseText和NewHistoricalKeyEvents的内容始终使用中文来输出。并且ResponseText内容尽量保持简短\n",
				}.Join(delimiter: "");
		}

		private string GetCurrentChatGPTModel()
		{
			Tools.UpdateApiConfigs();
			if (RimGPTMod.Settings.userApiConfigs == null || RimGPTMod.Settings.userApiConfigs.Any(a => a.Active) == false)
				return "";

			var activeUserConfig = RimGPTMod.Settings.userApiConfigs.FirstOrDefault(a => a.Active);
			OpenAIApi.SwitchConfig(activeUserConfig.Provider);


			if (activeUserConfig.ModelId?.Length == 0)
				return "";

			if (!activeUserConfig.UseSecondaryModel || activeUserConfig.SecondaryModelId?.Length == 0)
				return activeUserConfig.ModelId;

			modelSwitchCounter++;

			if (modelSwitchCounter == activeUserConfig.ModelSwitchRatio)
			{
				modelSwitchCounter = 0;

				if (Tools.DEBUG)
					Logger.Message("Switching to secondary model"); // TEMP
				return activeUserConfig.SecondaryModelId;
			}
			else
			{
				if (Tools.DEBUG)
					Logger.Message("Switching to primary model"); // TEMP
				return activeUserConfig.ModelId;
			}
		}
		private float CalculateFrequencyPenaltyBasedOnLevenshteinDistance(string source, string target)
		{
			// Kept running into a situation where the source was null, not sure if that's due to a provider or what.
			if (source == null || target == null)
			{
				Logger.Error($"Calculate FP Error: Null source or target. Source: {source}, Target: {target}");
				return default;
			}
			int levenshteinDistance = LanguageHelper.CalculateLevenshteinDistance(source, target);

			// You can adjust these constants based on the desired sensitivity.
			const float maxPenalty = 2.0f; // Maximum penalty when there is little to no change.
			const float minPenalty = 0f; // Minimum penalty when changes are significant.
			const int threshold = 30;      // Distance threshold for maximum penalty.

			// Apply maximum penalty when distance is below or equal to threshold.
			if (levenshteinDistance <= threshold)
				return maxPenalty;

			// Apply scaled penalty for distances greater than threshold.
			float penaltyScaleFactor = (float)(levenshteinDistance - threshold) / (Math.Max(source.Length, target.Length) - threshold);
			float frequencyPenalty = maxPenalty * (1 - penaltyScaleFactor);

			return Mathf.Clamp(frequencyPenalty, minPenalty, maxPenalty);
		}


		public async Task<string> Evaluate(Persona persona, IEnumerable<Phrase> observations, int retry = 0, string retryReason = "")
		{
			var activeConfig = RimGPTMod.Settings.userApiConfigs.FirstOrDefault(a => a.Active);

			var gameInput = new Input
			{
				ActivityFeed = observations.Select(o => o.text).ToList(),
				LastSpokenText = persona.lastSpokenText,
				ColonyRoster = RecordKeeper.ColonistDataSummary,
				ColonySetting = RecordKeeper.ColonySetting,
				ResearchSummary = RecordKeeper.ResearchDataSummary,
				ResourceData = RecordKeeper.ResourceData,
				RoomsSummary = RecordKeeper.RoomsDataSummary,
				EnergySummary = RecordKeeper.EnergySummary
			};

			var windowStack = Find.WindowStack;
			if (Current.Game == null && windowStack != null)
			{
				if (windowStack.focusedWindow is not Page page || page == null)
				{
					if (WorldRendererUtility.WorldRenderedNow)
						gameInput.CurrentWindow = "The player is selecting the start site";
					else
						gameInput.CurrentWindow = "The player is at the start screen";
				}
				else
				{
					var dialogType = page.GetType().Name.Replace("Page_", "");
					gameInput.CurrentWindow = $"The player is at the dialog {dialogType}";
				}
				// Due to async nature of the game, a reset of history and recordkeeper
				// may have slipped through the cracks by the time this function is called.
				// this is to ensure that if all else fails, we don't include any colony data and we clear history (as reset intended)
				if (gameInput.ColonySetting != "Unknown as of now..." && gameInput.CurrentWindow == "The player is at the start screen")
				{

					// I'm not sure why, but Personas are not being reset propery, they tend to have activityfeed of old stuff
					// and recordKeeper contains colony data still.  I"m guessing the reset unloads a bunch of stuff before
					// the actual reset could finish (or something...?) 
					// this ensures the reset happens
					Personas.Reset();

					// cheap imperfect heuristic to not include activities from the previous game.
					// the start screen is not that valueable for context anyway.  its the start screen.
					if (gameInput.ActivityFeed.Count > 0)
						gameInput.ActivityFeed = ["The player restarted the game"];
					gameInput.ColonyRoster = [];
					gameInput.ColonySetting = "The player restarted the game";
					gameInput.ResearchSummary = "";
					gameInput.ResourceData = "";
					gameInput.RoomsSummary = "";
					gameInput.EnergySummary = "";
					gameInput.PreviousHistoricalKeyEvents = [];
					ReplaceHistory("The Player restarted the game");
				}

			}

			var systemPrompt = SystemPrompt(persona);
			if (FrequencyPenalty > 1)
			{
				systemPrompt += "\n注意：你回答的内容太重复了，你需要根据你所拥有的信息做出一些新的点评。";
				systemPrompt += $"\n避免谈论任何以下内容相关的事情: {persona.lastSpokenText}";
				history.AddItem("我最近回答的内容太重复了，我需要避免重复上次所说的内容");
			}
			if (history.Count() > 5)
			{
				var newhistory = (await CondenseHistory(persona)).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
				ReplaceHistory(newhistory);
			}

			gameInput.PreviousHistoricalKeyEvents = history;
			var input = JsonConvert.SerializeObject(gameInput, settings);

			Logger.Message($"{(retry != 0 ? $"(retry:{retry} {retryReason})" : "")} prompt (FP:{FrequencyPenalty}) ({gameInput.ActivityFeed.Count()} activities) (persona:{persona.name}): {input}");

			List<string> jsonReady = ["1106", "0125"];

			var request = new CreateChatCompletionRequest()
			{
				Model = GetCurrentChatGPTModel(),
				ResponseFormat = jsonReady.Any(id => GetCurrentChatGPTModel().Contains(id)) ? new ResponseFormat { Type = "json_object" } : null,
				FrequencyPenalty = FrequencyPenalty,
				PresencePenalty = FrequencyPenalty,
				Temperature = 0.5f,
				Messages =
				[
					new ChatMessage() { Role = "system", Content = systemPrompt },
					new ChatMessage() { Role = "user", Content = input }
				]
			};

			if (Tools.DEBUG)
				Logger.Warning($"INPUT: {JsonConvert.SerializeObject(request, settings)}");

			var completionResponse = await OpenAIApi.CreateChatCompletion(request, error => Logger.Error(error));
			activeConfig.CharactersSent += systemPrompt.Length + input.Length;

			if (completionResponse.Choices?.Count > 0)
			{
				var response = (completionResponse.Choices[0].Message.Content ?? "");
				activeConfig.CharactersReceived += response.Length;
				response = response.Trim();
				var firstIdx = response.IndexOf("{");
				if (firstIdx >= 0)
				{
					var lastIndex = response.LastIndexOf("}");
					if (lastIndex >= 0)
						response = response.Substring(firstIdx, lastIndex - firstIdx + 1);
				}
				response = response.Replace("ResponseText:", "");
				if (Tools.DEBUG)
					Logger.Warning($"OUTPUT: {response}");

				Output output;
				if (string.IsNullOrEmpty(response))
					throw new InvalidOperationException("Response is empty or null.");
				try
				{
					if (response.Length > 0 && response[0] != '{')
						output = new Output { ResponseText = response, NewHistoricalKeyEvents = [] };
					else
						output = JsonConvert.DeserializeObject<Output>(response);
				}
				catch (JsonException jsonEx)
				{
					if (retry < maxRetries)
					{
						Logger.Error($"(retrying) ChatGPT malformed output: {jsonEx.Message}. Response was: {response}");
						return await Evaluate(persona, observations, ++retry, "malformed output");
					}
					else
					{
						Logger.Error($"(aborted) ChatGPT malformed output: {jsonEx.Message}. Response was: {response}");
						return null;
					}
				}
				try
				{
					if (gameInput.CurrentWindow != "The player is at the start screen")
					{
						var newhistory = output.NewHistoricalKeyEvents.ToList() ?? [];
						ReplaceHistory(newhistory);
					}
					var responseText = output.ResponseText?.Cleanup() ?? string.Empty;

					if (string.IsNullOrEmpty(responseText))
						throw new InvalidOperationException("Response text is null or empty after cleanup.");

					// Ideally we would want the last two things and call this sooner, but MEH.  
					FrequencyPenalty = CalculateFrequencyPenaltyBasedOnLevenshteinDistance(persona.lastSpokenText, responseText);
					if (FrequencyPenalty == 2 && retry < maxRetries)
						return await Evaluate(persona, observations, ++retry, "repetitive");

					// we're not repeating ourselves again.
					if (FrequencyPenalty == 2)
					{
						Logger.Message($"Skipped output due to repetitiveness. Response was {response}");
					}

					return responseText;
				}
				catch (Exception exception)
				{
					Logger.Error($"Error when processing output: [{exception.Message}] {exception.StackTrace} {exception.Source}");
				}
			}
			else if (Tools.DEBUG)
				Logger.Warning($"OUTPUT: null");

			return null;
		}
		public async Task<string> CondenseHistory(Persona persona)
		{
			// force secondary (better model)
			modelSwitchCounter = RimGPTMod.Settings.userApiConfigs.FirstOrDefault(a => a.Active).ModelSwitchRatio;
			var request = new CreateChatCompletionRequest()
			{
				Model = GetCurrentChatGPTModel(),
				Messages =
				[
					new ChatMessage() { Role = "system", Content = $"你是一个对抗系统，清理历史列表，目标是去除重复性并为以下角色保持叙述的新鲜感: {persona.personality}" },
					new ChatMessage() { Role = "user", Content =  "将以下事件总结成一个简洁的句子，侧重于异常值以减少对最显著主题的执着: " + String.Join("\n ", history)}
				]
			};


			var completionResponse = await OpenAIApi.CreateChatCompletion(request, error => Logger.Error(error));
			var response = (completionResponse.Choices[0].Message.Content ?? "");
			Logger.Message("Condensed History: " + response.ToString());
			return response.ToString(); // The condensed history summary
		}
		public void ReplaceHistory(string reason)
		{
			history = [reason];
		}

		public void ReplaceHistory(string[] reason)
		{
			history = [.. reason];
		}
		public void ReplaceHistory(List<string> reason)
		{
			history = reason;
		}

		public async Task<(string, string)> SimplePrompt(string input, UserApiConfig userApiConfig = null, string modelId = "")
		{
			var currentConfig = OpenAIApi.currentConfig;
			var currentUserConfig = RimGPTMod.Settings.userApiConfigs.FirstOrDefault(a => a.Provider == currentConfig.Provider.ToString());
			if (userApiConfig != null)
			{
				OpenAIApi.SwitchConfig(userApiConfig.Provider); // Switch if test comes through.
				currentUserConfig = userApiConfig;
				modelId ??= userApiConfig.ModelId;
			}
			else
			{
				modelId = GetCurrentChatGPTModel();
			}

			string requestError = null;
			var completionResponse = await OpenAIApi.CreateChatCompletion(new CreateChatCompletionRequest()
			{
				Model = modelId,
				Messages =
				[
					new ChatMessage() { Role = "system", Content = "You are a creative poet answering in 12 words or less." },
					new ChatMessage() { Role = "user", Content = input }
				]
			}, e => requestError = e);
			currentUserConfig.CharactersSent += input.Length;

			if (userApiConfig != null)
				OpenAIApi.currentConfig = currentConfig;

			if (completionResponse.Choices?.Count > 0)
			{
				var response = (completionResponse.Choices[0].Message.Content ?? "");
				currentUserConfig.CharactersReceived += response.Length;
				return (response, null);
			}

			return (null, requestError);
		}

		public static void TestKey(Action<string> callback, UserApiConfig userApiConfig, string modelId = "")
		{
			Tools.SafeAsync(async () =>
			{
				var prompt = "The player has just configured your OpenAI API key in the mod " +
					 "RimGPT for Rimworld. Greet them with a short response!";
				var dummyAI = new AI();
				var output = await dummyAI.SimplePrompt(prompt, userApiConfig, modelId);
				callback(output.Item1 ?? output.Item2);
			});
		}
	}
}