using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Microsoft.Extensions.Options;
using System.Text.Json;

// Single representative authoring lifecycle, exclusively in the guarded private copy.
var connection = await DisposableDatabase.Connection();
var factory = new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions {Active="test", Profiles=new(){["test"]=new(){ConnectionString=connection}}}));
var repo = new MobRepository(factory);
var assetService = new ItemAssetService(Options.Create(new AssetRootsOptions {Roots=new(){["game_client_assets"]=Path.GetFullPath(args[0])}}));
var validator = new MobDefinitionValidator(repo, assetService);
var appearance = new ActorAppearanceCatalogService(Options.Create(new AssetRootsOptions {Roots=new(){["game_client_assets"]=Path.GetFullPath(args[0])}}));
var service = new MobAuthoringService(repo, validator, new MobAuthoringRegistry(), assetService, appearance, new RiggedSpritePreviewResolver(appearance,assetService), Microsoft.Extensions.Logging.Abstractions.NullLogger<MobAuthoringService>.Instance);
var loadedOptions = await service.LoadOptionsAsync();
Check(loadedOptions.Succeeded && loadedOptions.Value!.HumanLayerAssets!["cape"].Contains("ice_armor"), "selector offers real cape art");
var detail = await service.LoadAsync("wizard");
Check(detail.Succeeded && detail.Value!.LayerAssetKeys!["cape"]=="ice_armor", "detail API exposes outfit");
var original = (await repo.LoadAsync("wizard"))!;
var draft = MobAuthoringService.FromRecord(original);
Check(draft.LayerAssetKeys!["cape"] == "ice_armor", "published wizard outfit loads");
var layers = new Dictionary<string,string>(draft.LayerAssetKeys) { ["cape"]="fire_armor" };
var edited = draft with {LayerAssetKeys=layers};
var options = new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower};
var request = JsonSerializer.Deserialize<SaveMobDraftRequest>(JsonSerializer.Serialize(edited,options))!;
var normalized = MobAuthoringService.Normalize(request);
Check(normalized.LayerAssetKeys!["cape"]=="fire_armor", "request normalization preserves outfit");
Check(!MobAuthoringService.EquivalentDraft(original,normalized),"outfit edit is meaningful");
Check(MobAuthoringService.ComputePreviewSignature("wizard","save_draft",draft,original.UpdatedAtUtc)!=MobAuthoringService.ComputePreviewSignature("wizard","save_draft",normalized,original.UpdatedAtUtc),"signature covers outfit");
var validation=await validator.ValidateAsync("wizard",normalized,original,true);
Check(validation.ValidForPublication, string.Join(";",validation.Messages.Select(m=>m.Message)));
var invalid = normalized with {LayerAssetKeys=new Dictionary<string,string>(layers) {["head"]="../missing"}};
Check(!(await validator.ValidateAsync("wizard",invalid,original,true)).ValidForDraft,"unsafe layer rejected");
var saved=await repo.SaveDraftAsync("wizard",normalized,original.UpdatedAtUtc);
var loaded=(await repo.LoadAsync("wizard"))!;
Check(MobAuthoringService.Equivalent(saved,loaded),"save/reload preserves every field");
var published=await repo.SetPublicationAsync("wizard","Published",saved.UpdatedAtUtc);
Check(MobAuthoringService.EquivalentDraft(published,normalized),"publish preserves outfit");
Check(published.RootLootTableId==original.RootLootTableId && published.PrimaryCombatProfile==original.PrimaryCombatProfile && published.MaxHealth==14,"approved loot and stats preserved");
// Leave the private fixture at its approved outfit for any retained inspection.
saved=await repo.SaveDraftAsync("wizard",draft,published.UpdatedAtUtc);
await repo.SetPublicationAsync("wizard","Published",saved.UpdatedAtUtc);
Console.WriteLine("PASS: wizard layer edit, API normalization/signature/validation, save/reload/publish; stats and loot unchanged; invalid path rejected.");
static void Check(bool ok,string label){if(!ok)throw new Exception(label);}
