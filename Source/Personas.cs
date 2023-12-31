﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimGPT
{
	public static class Personas
	{
		public static readonly int maxQueueSize = 3;
		public static bool IsAudioQueueFull => speechQueue.Count >= maxQueueSize;
		public static ConcurrentQueue<SpeechJob> speechQueue = new();
		public static string currentText = "";
		public static Persona currentSpeakingPersona = null;
		private static OrderedHashSet<Phrase> allPhrases = new OrderedHashSet<Phrase>();
		public static Persona lastSpeakingPersona = null;
		
		static Personas()
		{
			StartNextPersona();
			Tools.SafeLoop(() =>
			{
				foreach (var persona in RimGPTMod.Settings.personas)
					persona.Periodic();
			},
			1000);

			Tools.SafeLoop(async () =>
			{
				if (speechQueue.TryPeek(out var job) == false || job.completed == false)
				{
					//FileLog.Log($"...{(job == null ? "empty" : "job")}");
					await Tools.SafeWait(200);
					return true;
				}
				_ = speechQueue.TryDequeue(out job);
				//FileLog.Log($"SPEECH QUEUE -1 => {speechQueue.Count} -> play {job.persona?.name ?? "null"}: {job.audioClip != null}/{job.completed}");
				await job.Play(false);
				return false;
			});
		}
		public static void StartNextPersona(Persona lastSpeaker = null)
		{
			var candidates = RimGPTMod.Settings.personas.Where(p => p.nextPhraseTime > DateTime.Now);
			lastSpeakingPersona = lastSpeaker;
			Persona nextPersona;

			if (!candidates.Any())
			{
				// If there are no future phrase times, simply use the round-robin approach.
				int currentIndex = lastSpeaker != null ? RimGPTMod.Settings.personas.IndexOf(lastSpeaker) : 0;
				if (currentIndex == -1 || currentIndex >= RimGPTMod.Settings.personas.Count - 1)
					currentIndex = 0;
				else
					currentIndex++;

				nextPersona = RimGPTMod.Settings.personas[currentIndex];
			}
			else
			{
				// Select the candidate with the closest nextPhraseTime
				nextPersona = candidates.OrderBy(p => p.nextPhraseTime).First();
			}

			int transferCount = Math.Min(nextPersona.phrasesLimit, allPhrases.Count);

			var phrasesToTransfer = new List<Phrase>();
			for (int i = 0; i < transferCount; i++)
			{
				phrasesToTransfer.Add(allPhrases[i]);
			}

			allPhrases.RemoveFromStart(transferCount);

			foreach (var phrase in phrasesToTransfer)
			{
				nextPersona.phrases.Add(phrase);
			}

			// Add last spoken phrase from the previous speaker if it's not null
			if (lastSpeaker != null)
			{
				var lastSpokenPhrase = new Phrase
				{
					text = lastSpeaker.lastSpokenText,
					persona = lastSpeaker,
					priority = 3 // Assuming priority is always 3 for last spoken phrases
				};

				nextPersona.phrases.Add(lastSpokenPhrase);
			}
		}

		public static bool IsAnyCompletedJobWaiting()
		{
			return speechQueue.Any(job => job.readyForNextJob && !job.isPlaying);
		}

		public static void Add(string text, int priority, Persona speaker = null)
		{
			var phrase = new Phrase(speaker, text, priority);
			var existingPhrase = allPhrases.FirstOrDefault(p => p.text == text);
			if (existingPhrase.text != null) return;
			Logger.Message(phrase.ToString());

			allPhrases.Add(phrase);

		}


		public static void RemoveSpeechDelayForPersona(Persona persona)
		{
			lock (speechQueue)
			{
				foreach (var job in speechQueue)
					if (job.persona == persona && job.doneCallback != null)
					{
						var callback = job.doneCallback;
						job.doneCallback = null;
						callback();
					}
			}
		}

		public static void Reset(params string[] reason)
		{
			lock (speechQueue)
			{
				speechQueue.Clear();
				foreach (var persona in RimGPTMod.Settings.personas)
					persona.Reset(reason);
			}
		}

		public static void CreateSpeechJob(Persona persona, Phrase[] phrases, Action<string> errorCallback, Action doneCallback)
		{

			Logger.Message($"[{persona.name}] Speech Job: {string.Join(", ", phrases.Select(ph => ph.ToString()))}");

			lock (speechQueue)
			{
				if (IsAudioQueueFull == false && RimGPTMod.Settings.azureSpeechKey != "" && RimGPTMod.Settings.azureSpeechRegion != "")
				{
					var filteredPhrases = phrases.Where(obs => obs.persona?.name != persona.name).ToArray();
					var job = new SpeechJob(persona, filteredPhrases, errorCallback, doneCallback);
					speechQueue.Enqueue(job);
					//FileLog.Log($"SPEECH QUEUE +1 => {speechQueue.Count}");
				}
				else
					doneCallback();
			}
		}

		public static void UpdateVoiceInformation()
		{
			TTS.voices = new Voice[0];
			if (RimGPTMod.Settings.azureSpeechKey == "" || RimGPTMod.Settings.azureSpeechRegion == "")
				return;

			Tools.SafeAsync(async () =>
			{
				TTS.voices = await TTS.DispatchFormPost<Voice[]>($"{TTS.APIURL}/voices/list", null, true, null);
				foreach (var persona in RimGPTMod.Settings.personas)
				{
					var voiceLanguage = Tools.VoiceLanguage(persona);
					var currentVoice = Voice.From(persona.azureVoice);
					if (currentVoice != null && currentVoice.LocaleName.Contains(voiceLanguage) == false)
					{
						currentVoice = TTS.voices
							.Where(voice => voice.LocaleName.Contains(voiceLanguage))
							.OrderBy(voice => voice.DisplayName)
							.FirstOrDefault();
						persona.azureVoice = currentVoice?.ShortName ?? "";
						persona.azureVoiceStyle = "default";
					}
				}
			});
		}
	}
}