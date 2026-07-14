# OpenGameMate Demo Recording Guide

This guide produces a real 20–40 second demonstration of the core loop without exposing accounts, private conversations, notifications, or tokens. Do not simulate an upload or AI response; every visible event should come from an actual OpenGameMate run.

## Before recording

- Use a test Windows profile and a ChatGPT conversation created only for the demo.
- Use a non-sensitive game scene with no player names, server addresses, chat messages, account IDs, or paid-item balances on screen.
- Disable desktop and game notifications. Close email, messaging, password managers, terminals, logs, and developer tools.
- Keep browser chrome, account menus, email addresses, and the WebView2 profile directory outside the crop.
- Set the OpenGameMate idle wait to 10 seconds so the complete flow fits within 20–40 seconds.
- Use headphones to prevent game audio from feeding back into the microphone.
- Record a dry run, then inspect every frame and the audio before publishing.

## 30-second recording script

| Time | What the viewer sees and hears |
| --- | --- |
| 0–5 s | The player moves normally in a safe game scene while ChatGPT Voice finishes one brief, natural comment. |
| 5–15 s | The player keeps playing, but both sides pause speaking. Keep the game moving so this does not look like a frozen mockup. |
| 15–19 s | Show the real OpenGameMate state change indicating that the continuous idle wait completed and the latest screen was sent. A short factual caption such as “Idle detected · latest screen sent” may be added only if it matches the recorded event. |
| 19–27 s | ChatGPT Voice naturally reacts to something visible in the game screen, teases the player, or starts a relevant topic. Do not subtitle or expose unrelated prior chat history. |
| 27–32 s | The player replies and continues playing, demonstrating that the conversation continues after the screen update. |

## Suggested framing

- Make gameplay the largest visual area.
- Show only the minimum OpenGameMate status needed to prove the idle-to-send transition.
- Use the ChatGPT Voice audio and a neutral Voice-state crop; avoid showing account identity or unrelated conversation history.
- If a cut is necessary, label it plainly. Do not splice unrelated uploads or responses together as one automatic event.

## Final privacy and accuracy review

Before committing a GIF or linking a video, verify that it contains none of the following:

- account names, email addresses, avatars tied to a private identity, cookies, tokens, QR codes, or browser profile paths;
- private chat history, model replies unrelated to the demo, notifications, game chat, friend lists, or server addresses;
- claims of an official relationship, universal game support, certain compatibility, no platform risk, or unrestricted usage;
- simulated OpenGameMate states, a manually uploaded image presented as automatic, or an AI response recorded from a different run.

Recommended output: MP4 or WebM at 1080p, 30 fps, 20–40 seconds, plus an optional GIF under GitHub's practical size limits. Keep the original recording outside the repository if it contains material that must be cropped or redacted.

## 中文录制要点

演示应完整表现：玩家正常游戏 → 双方暂时沉默 → OpenGameMate 完成连续空闲检测 → 自动发送最新画面 → ChatGPT 根据画面自然接话或主动开启话题 → 玩家继续交流。所有步骤都必须来自同一次真实运行；发布前逐帧检查账号、邮箱、通知、聊天、Cookie、Token、服务器地址和其他私人信息。
