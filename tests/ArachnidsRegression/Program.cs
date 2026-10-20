using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

// Exercise the existing dynamic family/specimen lifecycle in a disposable database.
var connection=await DisposableDatabase.Connection();
var factory=new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions {Active="test",Profiles=new(){["test"]=new(){ConnectionString=connection}}}));
var repository=new LoreRepository(factory);var publisher=new RecordingPublisher();
var service=new LoreAuthoringService(repository,publisher,NullLogger<LoreAuthoringService>.Instance);
var current=(await repository.LoadAsync("v1",default))!;
Check(current.Draft.FindFamily("arachnids") is {RequiredLevel:7} && current.Draft.FindSpecimen("spider_fang") is {RequiredLevel:1,BaseXp:20,MasteryPoints:4},"dynamic family/specimen load");
Check(current.MasteryPreview.Count(m=>m.FamilyId=="arachnids")==180,"shared 180 milestone preview");
Check((await repository.ValidatePublishedAsync(current.Draft,default)).Count==0,"published item and mob references validate");
await using var db=NpgsqlDataSource.Create(connection);
async Task<string> Published() {await using var c=db.CreateCommand("SELECT published_settings::text FROM lore_definitions WHERE definition_id='v1'");return (string)(await c.ExecuteScalarAsync())!;}
var before=await Published();
// Change an ordinary focus field; family/specimen rows must survive the standard editor payload.
var edited=current.Draft with {FishingFocusLevel=8};
foreach(var operation in new[]{"save_draft","publish"})
{
 var request=new LoreRequest(edited,current.UpdatedAtUtc,null,operation);
 var preview=await service.PreviewAsync("v1",request,default);
 Check(preview.Succeeded && preview.Value!.Applicable,"ordinary lifecycle preview validates");
 var result=await service.MutateAsync("v1",operation,request with {PreviewSignature=preview.Value!.PreviewSignature},default);
 Check(result.Succeeded,"ordinary lifecycle saves and reloads");current=result.Value!.Definition!;
 if(operation=="save_draft")Check(await Published()==before,"saving draft preserves independent published document");
}
Check(current.Draft.FindSpecimen("spider_fang") is {MasteryPoints:4} && publisher.Calls==2,"existing Lore catalog export route receives both refreshes");
Console.WriteLine("PASS Arachnids authoring through shared family/specimen lifecycle");
static void Check(bool value,string message){if(!value)throw new Exception(message);}
sealed class RecordingPublisher:IRuntimeCatalogPublisher
{
 public int Calls;
 public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope,CancellationToken cancellationToken)
 {if(scope!=RuntimeCatalogPublicationScope.Lore)throw new Exception("Unexpected catalog scope");Calls++;return Task.FromResult<IReadOnlyList<ApiError>>([]);}
}
