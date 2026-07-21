# Grounded AI Chat

WAID's chat assistant answers questions from locally persisted scan findings, diagnosis reports, and repair history. It works offline through a deterministic provider and exposes a provider interface for an optional local model. No cloud provider is enabled.

Retrieval, prompt construction, provider execution, safety validation, persistence, and UI rendering are separate services. Retrieved records are treated as untrusted data, normalized before prompt construction, and attached to assistant messages as inspectable evidence. Machine-specific answers must cite at least one retrieved record using `[evidence:id]`; uncited provider output is rejected.

The safety layer limits question length, redacts user names and common secret assignments, neutralizes prompt-injection phrases in retrieved evidence, bounds provider execution, and falls back to the offline provider on provider failure or timeout. Caller cancellation is preserved. Chat has no repair executor dependency and free text cannot authorize or execute a repair.

Conversations persist provider/model metadata, confidence, evidence references, export state, and timestamps in SQLite schema 15. Deletion clears message content and creates a durable deletion tombstone; deleted conversations cannot be reopened or exported.

The AI Chat navigation page shows offline/provider state, messages, confidence, and evidence chips. Any suggested action remains informational and must go through WAID's separate repair review and explicit-approval workflow.
