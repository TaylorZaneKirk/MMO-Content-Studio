using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using MMO.Project.Prototype.Server.Game;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

// Isolated disposable fixture only; never reads the live connection settings.
var connection=await DisposableDatabase.Connection();
var factory=new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions {Active="test",Profiles=new(){["test"]=new(){ConnectionString=connection}}}));
var repository=new CookingRepository(factory);var publisher=new RecordingPublisher();
var service=new CookingAuthoringService(repository,publisher,NullLogger<CookingAuthoringService>.Instance);
var original=(await repository.LoadAsync("preparation_mix",default))!;var recipe=original.Draft;
Check(recipe.AdditionalOutputs?.Count==2 && recipe.DurationMs==1200 && recipe.Xp==0,"load every preparation field");
var current=original;
async Task Apply(string action,CookingRecipe value)
{
 var request=new CookingRequest(value,current.UpdatedAtUtc,null,action);
 var preview=await service.PreviewAsync(value.RecipeId,request,default);Check(preview.Succeeded,"preview validates preparation");
 var result=await service.MutateAsync(value.RecipeId,action,request with {PreviewSignature=preview.Value!.PreviewSignature},default);
 Check(result.Succeeded,"versioned preparation mutation");current=result.Value!.Definition!;
}
await Apply("save_draft",recipe with {DurationMs=1800});
await using var db=NpgsqlDataSource.Create(connection);
await using(var c=db.CreateCommand("SELECT (published_recipe->>'duration_ms')::int FROM cooking_recipes WHERE recipe_id='preparation_mix'"))Check((int)(await c.ExecuteScalarAsync())! ==1200,"draft retains published duration");
await Apply("publish",current.Draft);
Check(publisher.Calls==1 && current.Draft.AdditionalOutputs?.Count==2,"publication preserves returned containers and exports");
await using(var c=db.CreateCommand("UPDATE item_definitions SET runtime_enabled=false WHERE item_id='empty_cooking_bucket'"))
{
 try{await c.ExecuteNonQueryAsync();throw new Exception("Unpublished output allowed");}catch(PostgresException e)when(e.SqlState=="P0001"){Console.WriteLine("PASS published additional output dependency guarded");}
}
var invalid=recipe with {AdditionalOutputs=[new("missing_container",1)]};
Check((await repository.ValidatePublishedAsync(invalid,default)).Count>0,"unpublished return rejected before publication");
await Apply("disable",current.Draft);
Check(!current.HasPublishedSnapshot && publisher.Calls==2,"disable uses ordinary catalog pipeline");
Console.WriteLine("PASS preparation authoring lifecycle and dependencies");
static void Check(bool v,string text){if(!v)throw new Exception(text);Console.WriteLine("PASS "+text);}
sealed class RecordingPublisher:IRuntimeCatalogPublisher
{
 public int Calls;
 public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope,CancellationToken cancellationToken)
 {if(scope!=RuntimeCatalogPublicationScope.Cooking)throw new Exception("Unexpected export scope");Calls++;return Task.FromResult<IReadOnlyList<ApiError>>([]);}
}
