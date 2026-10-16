using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

if(args.Length!=1)throw new ArgumentException("Supply game asset root.");
var connection=await DisposableDatabase.Connection();
var factory=new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions{Active="test",Profiles=new(){["test"]=new(){ConnectionString=connection}}}));
var repository=new CraftingRepository(factory);var publisher=new RecordingPublisher();
var assets=new ItemAssetService(Options.Create(new AssetRootsOptions{Roots=new(){["game_client_assets"]=args[0]}}));
var service=new CraftingAuthoringService(repository,assets,publisher,NullLogger<CraftingAuthoringService>.Instance);
var current=(await repository.LoadAsync("v1",default))!;var original=current.Draft;
Check(original.Arrows?.Iron.XpTenths==25,"arrow fields load through shared DTO");
await using var db=NpgsqlDataSource.Create(connection);
// A retained chair must not prevent unrelated arrow recipe tuning.
await using(var place=db.CreateCommand("INSERT INTO character_home_furniture(character_id,placement_id,definition_id,tile_x,tile_y,facing) SELECT id,@placement,'v1',11,7,'east' FROM characters ORDER BY id LIMIT 1 ON CONFLICT DO NOTHING"))
{place.Parameters.AddWithValue("placement",Guid.NewGuid());await place.ExecuteNonQueryAsync();}
async Task Apply(string operation, MMO.Project.Crafting.CraftingSettings settings)
{
 var request=new CraftingRequest(settings,current.UpdatedAtUtc,null,operation);
 var preview=await service.PreviewAsync("v1",request,default);Check(preview.Succeeded&&preview.Value!.Applicable,"versioned preview "+operation);
 var result=await service.MutateAsync("v1",operation,request with {PreviewSignature=preview.Value!.PreviewSignature},default);Check(result.Succeeded,"mutation "+operation);current=result.Value!.Definition!;
}
await Apply("save_draft",original with {Arrows=original.Arrows! with {Iron=original.Arrows.Iron with {XpTenths=26}}});
await using(var q=db.CreateCommand("SELECT (published_settings->'arrows'->'iron'->>'xp_tenths')::int FROM crafting_definitions"))Check((int)(await q.ExecuteScalarAsync())! ==25,"draft does not alter publication");
await Apply("publish",current.Draft);Check(publisher.Calls==1,"ordinary crafting exporter requested");
Check((await repository.ValidateRetainedFurnitureAsync("publish",current.Draft with {ChairItemId="bronze_arrows"},default)).Count>0,"retained chair binding still protected");
Check((await repository.ValidatePublishedAsync(current.Draft with {Arrows=current.Draft.Arrows! with {KnifeItemId="feather"}},default)).Count>0,"duplicate/wrong tool shape rejected");
await using(var q=db.CreateCommand("UPDATE item_definitions SET runtime_enabled=false WHERE item_id='feather'"))
{try{await q.ExecuteNonQueryAsync();throw new Exception("published dependency lost");}catch(PostgresException e)when(e.SqlState=="P0001"){}}
var stale=new CraftingRequest(original,DateTimeOffset.MinValue,null,"save_draft");var staleResult=await service.PreviewAsync("v1",stale,default);Check(!staleResult.Value!.Applicable,"stale version rejected");
await Apply("save_draft",original);await Apply("publish",current.Draft);
Check(current.Draft.Arrows==original.Arrows&&current.Draft.ChairXpTenths==original.ChairXpTenths,"restore fixture with furniture preserved");
Console.WriteLine("PASS arrow authoring draft/publish/export, retained furniture, stale versions and published dependencies");
static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
sealed class RecordingPublisher:IRuntimeCatalogPublisher
{
 public int Calls;
 public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope,CancellationToken ct)
 {if(scope!=RuntimeCatalogPublicationScope.Crafting)throw new Exception("wrong export scope");Calls++;return Task.FromResult<IReadOnlyList<ApiError>>([]);}
}
