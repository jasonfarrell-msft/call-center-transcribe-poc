# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, Azure design, scope, decisions | Athrun (Lead) | Service topology, ACS + AI Foundry design, model selection, trade-offs, code review |
| Frontend / web UI | Lunamaria | Agent-assist dashboard, live transcript panel, sentiment/churn gauges, knowledge cards, next-step UI |
| Backend / pipeline / APIs | Meyrin | Real-time WebSocket relay, AI pipeline orchestration, API endpoints, service wiring (C#/.NET) |
| ACS / telephony / live-call audio fork | Dyakka | Inbound call answering, Call Automation, media streaming, `AcsAudioSource`, dual-party call script + demo runbook |
| AI / ML / agents | Lacus | Diarization, translation, continual sentiment, churn-risk agent, RAG retrieval, next-best-action generation (MAI models) |
| Call-center / CX domain | Kira | Propane retention playbook, conversation workflows, mocked knowledge content, churn signal taxonomy, demo narrative |
| Testing & QA | Yzak | Test cases, demo-script validation, edge cases (non-English, multi-speaker, churn escalation), reviewer gate |
| Code review | Athrun | Review PRs, check quality, enforce reviewer gates |
| Scope & priorities | Athrun | What to build next, trade-offs, decisions |
| Session logging | Scribe | Automatic — never needs routing |
| Work queue / backlog monitoring | Ralph | Scan issues/PRs, keep the pipeline moving |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Athrun (Lead) |
| `squad:{name}` | Pick up issue and complete the work | Named member |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **Athrun** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.**
4. **When two agents could handle it**, pick the one whose domain is the primary concern. (Pipeline plumbing → Meyrin; model/agent logic → Lacus.)
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** Building a feature → spawn Yzak to write test cases from requirements simultaneously.
7. **Issue-labeled work** — `squad:{member}` routes to that member. Athrun handles all `squad` base-label triage.
