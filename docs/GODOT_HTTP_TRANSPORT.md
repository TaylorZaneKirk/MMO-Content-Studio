# Godot HTTP Transport

`AuthoringHttpTransport` owns the reusable local-host transport boundary:

- one in-flight `HTTPRequest`
- request IDs and API-version headers
- JSON serialization
- response-envelope parsing
- HTTP and application-error extraction
- API-version compatibility checks
- busy-state rejection and transport-level failure signals

`AuthoringHostClient` remains the compatibility facade used by the current UI.
It owns the startup sequence, feature operation names, and existing public
signals/methods, but delegates all HTTP and envelope handling to the transport.

Future workspace APIs should build on the transport rather than adding another
`HTTPRequest`, duplicating envelope parsing, or extending a transport enum in
multiple places. The next extraction can split item, consumable, equipment,
and mob operation routing behind feature-specific clients while preserving the
existing facade until editors migrate.
