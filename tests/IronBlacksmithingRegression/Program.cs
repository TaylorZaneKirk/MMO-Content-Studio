using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

var connection=await DisposableDatabase.Connection();
var factory=new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions {Active="test",Profiles=new(){["test"]=new(){ConnectionString=connection}}}));
var repository=new BlacksmithingRepository(factory);var publisher=new RecordingPublisher();
var service=new BlacksmithingAuthoringService(repository,publisher,NullLogger<BlacksmithingAuthoringService>.Instance);
var current=(await repository.LoadAsync("smelt_iron_bar",default))!;
Check(current.Draft.SuccessPercent==50,"load authored fixed chance");
async Task Apply(string action,int chance)
{
 var value=current.Draft with {SuccessPercent=chance};
 var request=new BlacksmithingRequest(value,current.UpdatedAtUtc,null,action);
 var preview=await service.PreviewAsync(value.RecipeId,request,default);
 Check(preview.Succeeded && preview.Value!.Applicable,"chance preview validates");
 var result=await service.MutateAsync(value.RecipeId,action,request with {PreviewSignature=preview.Value!.PreviewSignature},default);
 Check(result.Succeeded,"versioned chance mutation");current=result.Value!.Definition!;
}
await Apply("save_draft",75);
Check(current.Draft.SuccessPercent==75,"chance round-trips through database");
await Apply("save_draft",50);await Apply("publish",50);
Check(current.Draft.SuccessPercent==50 && publisher.Calls>0,"approved chance published through ordinary exporter boundary");
var forge=(await repository.LoadAsync("forge_iron_helm",default))!;
var invalid=new BlacksmithingRequest(forge.Draft with {SuccessPercent=50},forge.UpdatedAtUtc,null,"save_draft");
var rejected=await service.PreviewAsync(forge.DefinitionId,invalid,default);
Check(rejected.Succeeded && !rejected.Value!.Applicable,"authoring rejects random forging");
await using var db=NpgsqlDataSource.Create(connection);
await using(var c=db.CreateCommand("UPDATE blacksmithing_recipes SET success_percent=50 WHERE recipe_id='forge_iron_helm'"))
{
 try{await c.ExecuteNonQueryAsync();throw new Exception("constraint accepted random forging");}
 catch(PostgresException e) when(e.SqlState=="23514"){Console.WriteLine("PASS database rejects random forging");}
}
Console.WriteLine("PASS iron Studio lifecycle");
static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
sealed class RecordingPublisher:IRuntimeCatalogPublisher
{
 public int Calls;
 public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope,CancellationToken cancellationToken)
 {if(scope!=RuntimeCatalogPublicationScope.Blacksmithing)throw new Exception("Unexpected scope");Calls++;return Task.FromResult<IReadOnlyList<ApiError>>([]);}
}
