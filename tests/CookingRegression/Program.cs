using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using MMO.Project.Prototype.Server.Game;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Npgsql;

// This test deliberately cannot use the live database or publisher.
const string connection="Host=/tmp/apple-pie/pg-socket;Port=54584;Database=apple_pie_schema_review;Username=taylor;Pooling=false";
var factory=new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions { Active="test",Profiles=new(){["test"]=new(){ConnectionString=connection}} }));
var repository=new CookingRepository(factory);var publisher=new RecordingPublisher();
var service=new CookingAuthoringService(repository,publisher,NullLogger<CookingAuthoringService>.Instance);
var original=(await repository.LoadAsync("apple_pie",default))!;
Check(original.HasPublishedSnapshot && original.Draft.Ingredients.Count==2,"load complete recipe and publication snapshot");
var recipe=original.Draft with { RecipeId="pie_test" };
CookingDefinition? current=null;
async Task<AuthoringOperationResult<CookingMutation>> Apply(string action,CookingRecipe value)
{
    var request=new CookingRequest(value,current?.UpdatedAtUtc,null,action);
    var preview=await service.PreviewAsync(value.RecipeId,request,default);
    Check(preview.Succeeded,"preview returns validation");
    var result=await service.MutateAsync(value.RecipeId,action,request with { PreviewSignature=preview.Value!.PreviewSignature },default);
    if(result.Succeeded)current=result.Value!.Definition;
    return result;
}
Check((await Apply("save_draft",recipe)).Succeeded,"save new draft");
Check((await Apply("publish",recipe)).Succeeded && current!.HasPublishedSnapshot,"publish all recipe fields");
var old=current;
Check((await Apply("save_draft",recipe with { Xp=170 })).Succeeded && current!.HasPublishedSnapshot && current.PublicationState=="Draft","draft retains publication");
await using var db=NpgsqlDataSource.Create(connection);
await using(var q=db.CreateCommand("SELECT (published_recipe->>'xp')::int FROM cooking_recipes WHERE recipe_id='pie_test'"))
    Check((int)(await q.ExecuteScalarAsync())! ==130,"unpublished XP edit does not change exported snapshot");
var delete=await Apply("delete",current!.Draft);
Check(!delete.Succeeded && delete.Errors.Any(e=>e.Code=="cooking_still_published"),"draft cannot bypass Disable-before-Delete");
var stale=new CookingRequest(recipe,old!.UpdatedAtUtc,null,"save_draft");
var stalePreview=await service.PreviewAsync("pie_test",stale,default);
Check(!stalePreview.Value!.Applicable,"stale authoring version rejected");
await RejectSql("DELETE FROM cooking_recipes WHERE recipe_id='pie_test'");
await RejectSql("DELETE FROM item_definitions WHERE item_id='apple_pie'");
await RejectSql("UPDATE item_definitions SET runtime_enabled=false WHERE item_id='prepared_pastry_shell'");
Check((await Apply("disable",current.Draft)).Succeeded && !current!.HasPublishedSnapshot,"disable clears retained publication");
Check((await Apply("delete",current.Draft)).Succeeded && current is null,"disabled recipe deletes");
Check(publisher.Calls==3,"only publish/disable/delete request export");
var invalid=recipe with { Ingredients=[new("inventory_2_apple",1),new("inventory_2_apple",2)] };
Check(invalid.Validate().Count>0,"duplicate ingredient authoring rejected");
try { JsonSerializer.Deserialize<CookingRecipe>(JsonSerializer.Serialize(recipe).TrimEnd('}')+",\"silently_lost_field\":1}"); throw new Exception("Unknown field accepted"); }
catch(JsonException){Console.WriteLine("PASS unknown fields cannot silently disappear");}
Console.WriteLine("PASS Cooking Studio versioned draft/publish/disable/delete, retained snapshots and dependency guards.");
async Task RejectSql(string sql)
{
    try {await using var command=db.CreateCommand(sql);await command.ExecuteNonQueryAsync();}
    catch(PostgresException error) when(error.SqlState=="P0001"){Console.WriteLine("PASS durable publication guard");return;}
    throw new Exception("Publication guard accepted unsafe deletion/unpublication");
}
static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
sealed class RecordingPublisher:IRuntimeCatalogPublisher
{
    public int Calls;
    public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope,CancellationToken ct)
    {if(scope!=RuntimeCatalogPublicationScope.Cooking)throw new Exception("Unexpected export scope");Calls++;return Task.FromResult<IReadOnlyList<ApiError>>([]);}
}
