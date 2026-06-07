# Squad Team

> CallCenterTranscription — Real-time AI agent-assist for propane call centers (POC)

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| 🏗️ Athrun | Lead / Architect | `.squad/agents/athrun/charter.md` | active |
| ⚛️ Lunamaria | Frontend Dev | `.squad/agents/lunamaria/charter.md` | active |
| 🔧 Meyrin | Backend Dev | `.squad/agents/meyrin/charter.md` | active |
| 🧠 Lacus | AI Engineer | `.squad/agents/lacus/charter.md` | active |
| 🎧 Kira | Call Center / CX Specialist | `.squad/agents/kira/charter.md` | active |
| 🧪 Yzak | Tester / QA | `.squad/agents/yzak/charter.md` | active |
| 📞 Dyakka | ACS / Telephony Specialist | `.squad/agents/dyakka/charter.md` | active |
| 📋 Scribe | Session Logger | `.squad/agents/scribe/charter.md` | active |
| 🔄 Ralph | Work Monitor | — | 🔄 Monitor |

> Casting universe: **Mobile Suit Gundam SEED Destiny** (assignment `gundam-seed-destiny-2026-06-05`). Names are persistent identifiers — no role-play.

## Project Context

- **Project:** CallCenterTranscription
- **What it is:** A POC web app that assists call-center employees in real time. Azure Communication Services forks live call audio; an AI pipeline performs diarization, translation (non-English), continual sentiment analysis, churn-risk scoring (agentic), knowledge-article surfacing, and next-step suggestions.
- **Industry:** Propane retail. Churn = a customer deciding to stop buying propane from this company.
- **Scope:** Demonstrable POC. Data and knowledge are mocked to the simplest case. May be scripted.
- **Model policy:** Backend AI uses mid-tier models — **MAI models preferred** — via the latest GA Azure AI Foundry.
- **Created:** 2026-06-05
