using System.Text.Json;
using Microsoft.Extensions.Options;
using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;

// One real authoring roundtrip in a dedicated schema-only fixture; never live.
var connection = Environment.GetEnvironmentVariable("ConnectionStrings__PrototypeDatabase")!;
var settings = new NpgsqlConnectionStringBuilder(connection);
if (!(settings.Host ?? "").StartsWith("/tmp/mmo-db-woodcutter-") || settings.Database != "fixture")
    throw new InvalidOperationException("This proof requires its disposable woodcutter fixture.");
var factory = new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions
{ Active = "test", Profiles = new() { ["test"] = new() { ConnectionString = connection } } }));
var repo = new NpcRepository(factory);
var roots = Options.Create(new AssetRootsOptions { Roots = new() { ["game_client_assets"] = Path.GetFullPath(args[0]) } });
var assets = new ItemAssetService(roots);
var dialogues = new NpcDialogueReferenceProvider(roots);
var validator = new NpcDefinitionValidator(assets, dialogues);
var original = (await repo.LoadAsync("garrick_hale"))!;
Check(original.AmbientAnimation == "woodcutting", "SQL setting loads");
var draft = NpcAuthoringService.FromRecord(original);
var json = JsonSerializer.Serialize(draft);
var request = JsonSerializer.Deserialize<SaveNpcDraftRequest>(json)!;
var normalized = NpcAuthoringService.Normalize(request);
Check(normalized.AmbientAnimation == "woodcutting", "save contract normalization preserves choice");
var preview = JsonSerializer.Deserialize<PreviewNpcRequest>(json)!;
Check(NpcAuthoringService.Normalize(preview).AmbientAnimation == "woodcutting", "preview contract preserves choice");
var validation = await validator.ValidateAsync("garrick_hale", normalized, original, true);
Check(validation.ValidForPublication, string.Join("; ", validation.Messages.Select(m => m.Message)));
var off = normalized with { AmbientAnimation = "none" };
Check(!NpcAuthoringService.EquivalentDraft(original, off), "animation change is meaningful");
Check(NpcAuthoringService.ComputePreviewSignature("garrick_hale", "save_draft", normalized, original.UpdatedAtUtc) !=
      NpcAuthoringService.ComputePreviewSignature("garrick_hale", "save_draft", off, original.UpdatedAtUtc), "signature covers choice");
var invalid = normalized with { AmbientAnimation = "gather_rewards" };
Check(!(await validator.ValidateAsync("garrick_hale", invalid, original, false)).ValidForDraft, "unknown animation rejected");
var noAxe = normalized with { LayerAssetKeys = new Dictionary<string,string>(normalized.LayerAssetKeys!) { ["right_hand"] = "" } };
Check(!(await validator.ValidateAsync("garrick_hale", noAxe, original, true)).ValidForPublication, "chop without axe rejected");
var saved = await repo.SaveDraftAsync("garrick_hale", off, original.UpdatedAtUtc);
Check((await repo.LoadAsync("garrick_hale"))!.AmbientAnimation == "none", "save/reload None");
saved = await repo.SaveDraftAsync("garrick_hale", normalized, saved.UpdatedAtUtc);
var published = await repo.SetPublicationAsync("garrick_hale", "Published", saved.UpdatedAtUtc);
Check(NpcAuthoringService.EquivalentDraft(published, normalized), "publish retains complete approved definition");
Check((await repo.ListAsync(null)).Single().AmbientAnimation == "woodcutting", "list query retains setting");
Console.WriteLine("PASS: one Studio NPC contract, validation, signature, save/reload and publication roundtrip.");
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
