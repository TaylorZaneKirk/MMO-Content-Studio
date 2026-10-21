using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Npgsql;
var connection=await DisposableDatabase.Connection();
var factory=new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions {Active="test",Profiles=new(){["test"]=new(){ConnectionString=connection}}}));
var repository=new CraftingRepository(factory);
var publisher=new RecordingPublisher();
var assets=new ItemAssetService(Options.Create(new AssetRootsOptions {Roots=new(){["game_client_assets"]=args[0]}}));
var service=new CraftingAuthoringService(repository,assets,publisher,NullLogger<CraftingAuthoringService>.Instance);
var current=(await repository.LoadAsync("v1",default))!;
var original=current.Draft;
var edit=original with {Sawing=original.Sawing.Select(s=>s.RecipeId=="saw_oak_plank"?s with {DisplayName="Oak plank proof"}:s).ToArray()};
async Task Apply(string operation,MMO.Project.Crafting.CraftingSettings draft)
{
 var request=new CraftingRequest(draft,current.UpdatedAtUtc,null,operation);
 var preview=await service.PreviewAsync("v1",request,default);
 Check(preview.Succeeded&&preview.Value!.Applicable,"applicable "+operation+" preview: "+JsonSerializer.Serialize(preview));
 var result=await service.MutateAsync("v1",operation,request with {PreviewSignature=preview.Value!.PreviewSignature},default);
 Check(result.Succeeded,"verified "+operation);current=result.Value!.Definition!;
}
await Apply("save_draft",edit);
Check(JsonSerializer.Serialize(current.Draft)==JsonSerializer.Serialize(edit),"complete sawing and furniture round trip");
await using var db=NpgsqlDataSource.Create(connection);
await using(var q=db.CreateCommand("SELECT published_settings->'sawing'->1->>'display_name' FROM crafting_definitions WHERE definition_id='v1'"))
 Check((string)(await q.ExecuteScalarAsync())! == "Saw oak plank","draft leaves publication unchanged");
await Apply("publish",edit);
await Apply("save_draft",original);await Apply("publish",original);
Check(publisher.Calls==2,"only publish requests Crafting export");
foreach(var sql in new[]{"UPDATE item_definitions SET runtime_enabled=false WHERE item_id='oak_logs'","UPDATE crafting_definitions SET published_settings=jsonb_set(published_settings,'{sawing,1,level}','0') WHERE definition_id='v1'"})
{
 bool rejected=false;try{await using var q=db.CreateCommand(sql);await q.ExecuteNonQueryAsync();}catch(PostgresException){rejected=true;}
 Check(rejected,"durable sawing publication guard");
}
Console.WriteLine("PASS Oak Studio authoring lifecycle");
static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message.Split(':')[0]);}
sealed class RecordingPublisher:IRuntimeCatalogPublisher
{
 public int Calls;
 public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope,CancellationToken ct)
 {if(scope!=RuntimeCatalogPublicationScope.Crafting)throw new Exception("Wrong scope");Calls++;return Task.FromResult<IReadOnlyList<ApiError>>([]);}
}
