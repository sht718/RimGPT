# RimGPT

<img src="https://github.com/pardeike/RimGPT/raw/master/About/Preview.png"/>

本地化运行和汉化说明
- 不再需要使用OpenAI的tokens和Azure的key(他们无法被正常访问)
- 将原有的https://{region}.tts.speech.microsoft.com/cognitiveservices重定向至本地  http://127.0.0.1:9880/ 。使用本地运行的[GPT-SoVITS](https://github.com/RVC-Boss/GPT-SoVITS)接口替代Azure的TTS
- 汉化LLM的提示词模板，使用[llama3-8b-chinese](https://ollama.com/wangshenzhi/llama3-8b-chinese-chat-ollama-q4/)可在本地进行中文对话。`ollama run wangshenzhi/llama3-8b-chinese-chat-ollama-q4`
- GPT-SoVITS和llama3-8b-chinese int4一起累计峰值消耗显存12G以上，因此使用大于12G显存的N卡获取较好的AI对话体验。(4060Ti 16g可以流畅运行)
- 这是我的评论员设定：**你是一名正在观看玩家玩流行游戏《Rimworld》的评论员。你会根据游戏殖民地的各种现状给出点评，有时候你会对殖民地的发展提出一些建议。始终使用中文进行对话，用中文回答。**

(c) 2024 zuojianghua, for localized operation and Chinese translation.

ChatGPT commentator using Azure natural voices.

Powered by
- [Harmony](https://github.com/pardeike/Harmony)
- [OpenAI](https://openai.com)
- [Microsoft Azure](https://azure.microsoft.com)

**Note: Using any other service, especially local AI is making the experience worse. You've been warned.**

(c) 2023 Andreas Pardeike, Matt McFarland and Steven Dickinson
