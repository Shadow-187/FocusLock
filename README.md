# FocusLock

I kept losing study sessions to Discord and YouTube, so I built this. FocusLock is a small Windows app (C#, .NET 8, WinForms) that locks me into a study session. It blocks the apps and sites I waste time on, and when the time is up it quizzes me on what I just studied.

I mostly use it for TryHackMe and my home lab, which is why VMware, Kali and the rest of my lab tools can never get blocked by accident.

## What it does

When I start a session, FocusLock:

- closes apps like Discord, Steam and games if I open them (it checks every 2 seconds)
- blocks sites like YouTube, Reddit and TikTok through the Windows hosts file
- puts a countdown in the corner that I can't close until time is up
- saves the session to disk, so closing or killing the app doesn't get me out of it

Before I start studying I get a short warm-up quiz on my *last* session. When the timer ends I write down what I learned, and then I get a quiz on that. Those notes are also what the next warm-up is built from. The scores get saved, so after a while I can see what actually stuck and what didn't.

The questions are generated with the Claude API if there's a key set. Without a key (or without internet) it falls back to a simpler recall quiz where I write what I remember and grade myself against my notes.

## A few security decisions

Since I'm studying IT security I tried to think a bit about how the app could break things or leak stuff:

- **The API key is never stored in plain text.** You can set it as the `ANTHROPIC_API_KEY` environment variable, or paste it into the app. If you paste it, it's encrypted with Windows DPAPI (`Secrets.cs`) before it's saved, tied to your Windows account. So if someone copies `settings.json` to another computer or user, the key is just useless bytes. Older versions saved it in plain text, so the app encrypts an old key automatically the first time it starts and rewrites the file.
- **It only touches its own lines in the hosts file.** Everything FocusLock adds sits between a start marker and an end marker, and only that block gets removed afterwards.
- **It can't kill the wrong process.** There's an allowlist (`explorer`, `dwm`, `csrss`, `winlogon`, `svchost`, browsers, VS Code, the app itself...) that's checked before anything gets closed. Anything with "kali", "vmware", "tryhackme" or "claude" in the name is protected too, even if you type it into the block list.
- **It needs admin, but only for one reason.** Editing the hosts file requires it, so that's declared in `app.manifest`. If you say no to UAC, app blocking still works but website blocking doesn't.

## What it can't stop

To be honest, it's not hard to get around if you really want to:

- DNS over HTTPS in Chrome/Edge skips the hosts file. Turn off "Use secure DNS" in the browser settings.
- Tabs that are already open might keep working until you reload them.
- Task Manager can kill it. The sites stay blocked until you open it again, though.
- Some antivirus programs warn when the hosts file changes. That's this app.

There's an emergency stop too, but you have to type a long phrase to use it. That's on purpose.

## Running it

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
cd src/FocusLock
dotnet run
```

If you want a single .exe to pin to the taskbar:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

For the AI quizzes, set your key once (get one at console.anthropic.com):

```powershell
setx ANTHROPIC_API_KEY "your-key-here"
```

You can change the block lists on the start screen. The model and the number of questions are in `settings.json` (`QuizModel`, `QuizQuestions`).

## The code

- `Blocker.cs` closes apps, edits the hosts file and holds the protected list
- `SessionForm.cs` is the countdown and the session loop
- `SetupForm.cs` is the start screen
- `QuizGenerator.cs` calls the Claude API and builds the questions
- `QuizForm.cs` shows the quiz (both modes)
- `Secrets.cs` encrypts and decrypts the API key with DPAPI
- `Storage.cs` saves settings, the running session and history as JSON
- `Program.cs` starts everything and resumes a session if one is running

MIT licensed, do whatever you want with it.
