using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using MMO.Project.Prototype.Server.Game;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Npgsql;

// Real authoring lifecycle against the workflow-owned private fixture; publisher is inert.
var connection = await DisposableDatabase.Connection();
var factory = new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions
{ Active="test", Profiles=new() { ["test"]=new() { ConnectionString=connection } } }));
var repository = new FishingRepository(factory);
var publisher = new RecordingPublisher();
var service = new FishingAuthoringService(repository,publisher,NullLogger<FishingAuthoringService>.Instance);
await using var db = NpgsqlDataSource.Create(connection);
var original = (await repository.LoadAsync("bait",default))!;
Check(original.HasPublishedSnapshot && original.Draft.Catches.Select(c=>c.ItemId).SequenceEqual(new[]{"herring","sardine"}), "load ordered existing Bait facts");
var method = original.Draft with { ActionId="test_fly",DisplayName="Test fly",IntervalMs=3600,ConsumableItemId="feather",ConsumeQuantity=2 };
FishingDefinition? current=null;
async Task<AuthoringOperationResult<FishingMutation>> Apply(string operation,FishingMethod draft)
{
    var request=new FishingRequest(draft,current?.UpdatedAtUtc,null,operation);
    var preview=await service.PreviewAsync(draft.ActionId,request,default);
    Check(preview.Succeeded,"preview returns validation");
    var result=await service.MutateAsync(draft.ActionId,operation,request with { PreviewSignature=preview.Value!.PreviewSignature },default);
    if(result.Succeeded)current=result.Value!.Definition;
    return result;
}
Check((await Apply("save_draft",method)).Succeeded,"new method saves");
Check(JsonSerializer.Serialize(current!.Draft)==JsonSerializer.Serialize(method),"all method fields round trip");
Check((await Apply("publish",method)).Succeeded && current!.HasPublishedSnapshot,"method publishes");
var before=current;
var edited=method with { IntervalMs=4200,Catches=method.Catches.Reverse().ToArray() };
Check((await Apply("save_draft",edited)).Succeeded && current!.HasPublishedSnapshot && current.PublicationState=="Draft","draft retains independent published snapshot");
await using(var q=db.CreateCommand("SELECT published_method::text FROM fishing_methods WHERE action_id='test_fly'"))
{
    var published=JsonSerializer.Deserialize<FishingMethod>((string)(await q.ExecuteScalarAsync())!)!;
    Check(published.IntervalMs==3600 && published.Catches[0].ItemId=="herring","unpublished timing/order edits do not alter export source");
}
var stale=new FishingRequest(edited,before!.UpdatedAtUtc,null,"save_draft");
Check(!(await service.PreviewAsync("test_fly",stale,default)).Value!.Applicable,"stale version rejected");
Check(!(await Apply("delete",edited)).Succeeded,"published snapshot requires disable before delete");
await RejectSql("UPDATE fishing_methods SET action_id='renamed_bait',draft=jsonb_set(draft,'{action_id}','\"renamed_bait\"'),published_method=jsonb_set(published_method,'{action_id}','\"renamed_bait\"') WHERE action_id='bait'");
await RejectSql("UPDATE fishing_methods SET published_method=jsonb_set(published_method,'{catches,0,item_id}','\"feather\"') WHERE action_id='test_fly'");
Check((method with { Catches=[method.Catches[0] with { ItemId="feather" }] }).Validate().Count>0,"catch cannot reuse consumed identity");
await RejectSql("DELETE FROM fishing_methods WHERE action_id='test_fly'");
await RejectSql("UPDATE item_definitions SET runtime_enabled=false WHERE item_id='feather'");
await RejectSql("UPDATE item_definitions SET stackable=false WHERE item_id='feather'");
await RejectSql("UPDATE fishing_methods SET published_method=NULL WHERE action_id='bait'");
await RejectSql("UPDATE fishing_methods SET published_method=published_method-'interval_ms' WHERE action_id='test_fly'");
await RejectSql("UPDATE fishing_methods SET published_method=jsonb_set(published_method,'{catches,0,success_high}','999') WHERE action_id='test_fly'");
Check((await Apply("disable",edited)).Succeeded && !current!.HasPublishedSnapshot,"disable clears snapshot");
await RejectSql("INSERT INTO world_object_interactions(definition_id,interaction_order,action_id,label,is_default) VALUES('starter_fishing_spot',99,'test_fly','Test',false)");
Check((await Apply("delete",edited)).Succeeded && current is null,"disabled unreferenced method deletes");
Check(publisher.Calls==3,"only publish/disable/delete request Fishing export");
Check((method with {ActionId="cook"}).Validate().Count>0,"method cannot shadow another gameplay action");
Check((method with {Catches=[method.Catches[0],method.Catches[0]]}).Validate().Count>0,"duplicate catch rows rejected");
try { JsonSerializer.Deserialize<FishingMethod>(JsonSerializer.Serialize(method).TrimEnd('}')+",\"unknown_field\":1}");throw new Exception("Unknown field accepted"); }
catch(JsonException){Console.WriteLine("PASS unknown authored fields rejected");}
// Publication holds referenced item rows until commit. A concurrent disable must
// wait, then fail against the newly published snapshot rather than commit a race.
await using (var seed = db.CreateCommand("INSERT INTO item_definitions SELECT (jsonb_populate_record(NULL::item_definitions,to_jsonb(i)||'{\"item_id\":\"fishing_test_tool\",\"item_name\":\"Fishing test tool\"}'::jsonb)).* FROM item_definitions i WHERE item_id='inventory_332_fishing_pole'"))
    await seed.ExecuteNonQueryAsync();
var racing = method with { ActionId="test_race",ToolItemId="fishing_test_tool" };
await repository.MutateAsync("test_race","save_draft",racing,null,default);
await using (var publishing = await db.OpenConnectionAsync())
await using (var transaction = await publishing.BeginTransactionAsync())
{
    await using (var publish = new NpgsqlCommand("UPDATE fishing_methods SET published_method=draft,publication_state='Published' WHERE action_id='test_race'; SET CONSTRAINTS ALL IMMEDIATE",publishing,transaction))
        await publish.ExecuteNonQueryAsync();
    var disabling = RejectSql("UPDATE item_definitions SET runtime_enabled=false WHERE item_id='fishing_test_tool'");
    await Task.Delay(150);
    Check(!disabling.IsCompleted,"concurrent item disable waits for publication dependency lock");
    await transaction.CommitAsync();
    await disabling;
}
await using (var cleanup = db.CreateCommand("UPDATE fishing_methods SET published_method=NULL WHERE action_id='test_race'")) await cleanup.ExecuteNonQueryAsync();
await using (var cleanup = db.CreateCommand("DELETE FROM fishing_methods WHERE action_id='test_race'; DELETE FROM item_definitions WHERE item_id='fishing_test_tool'")) await cleanup.ExecuteNonQueryAsync();
Console.WriteLine("PASS Fishing Studio draft/preview/publish/disable/delete, ordering, dependency guards and retained snapshots.");
async Task RejectSql(string sql)
{
    try { await using var command=db.CreateCommand(sql);await command.ExecuteNonQueryAsync(); }
    catch(PostgresException error) when(error.SqlState is "P0001" or "23514") {Console.WriteLine("PASS durable publication guard");return;}
    throw new Exception("Unsafe publication mutation was accepted");
}
static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
sealed class RecordingPublisher:IRuntimeCatalogPublisher
{
    public int Calls;
    public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope,CancellationToken ct)
    {if(scope!=RuntimeCatalogPublicationScope.Fishing)throw new Exception("Unexpected export scope");Calls++;return Task.FromResult<IReadOnlyList<ApiError>>([]);}
}
