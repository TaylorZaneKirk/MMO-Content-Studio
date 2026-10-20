using MMO.ContentStudio.AuthoringHost.Configuration;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Text.Json;

var connection=await DisposableDatabase.Connection();
var factory=new AuthoringDatabaseConnectionFactory(Options.Create(new ConnectionProfilesOptions {Active="test",Profiles=new(){["test"]=new(){ConnectionString=connection}}}));
var publisher=new RecordingPublisher();
var alchemy=new AlchemyRepository(factory);
var service=new AlchemyAuthoringService(alchemy,publisher,NullLogger<AlchemyAuthoringService>.Instance);
var original=(await alchemy.LoadAsync("v1",default))!;
Check(original.Draft.Recipes.Count==6 && original.Draft.Potions.Count==3,"all authored recipes and potion kinds load");
var recipes=original.Draft.Recipes.ToArray();recipes[2]=recipes[2] with {DisplayName="Prepare Emberroot proof"};
var draft=original.Draft with {Recipes=recipes};
var request=new AlchemyRequest(draft,original.UpdatedAtUtc,null,"save_draft");
var preview=await service.PreviewAsync("v1",request,default);Check(preview.Succeeded && preview.Value!.Applicable,"preview nested recipe edit");
var saved=await service.MutateAsync("v1","save_draft",request with {PreviewSignature=preview.Value!.PreviewSignature},default);
Check(saved.Succeeded && saved.Value!.Definition!.Draft.Recipes[2].DisplayName==recipes[2].DisplayName,"save and reload nested recipe edit");
request=new(saved.Value!.Definition!.Draft,saved.Value.Definition.UpdatedAtUtc,null,"publish");
preview=await service.PreviewAsync("v1",request,default);
Check((await service.MutateAsync("v1","publish",request with {PreviewSignature=preview.Value!.PreviewSignature},default)).Succeeded,"publish validated nested rows");
var farming=new FarmingRepository(factory);var farmService=new FarmingAuthoringService(farming,publisher,NullLogger<FarmingAuthoringService>.Instance);
var farm=(await farming.LoadAsync("flowers",default))!;
Check(farm.Draft.Crops.Count==3,"all crops load");
var crops=farm.Draft.Crops.ToArray();crops[1]=crops[1] with {DisplayName="Emberroot proof"};
var fr=new FarmingRequest(farm.Draft with {Crops=crops},farm.UpdatedAtUtc,null,"save_draft");
var fp=await farmService.PreviewAsync("flowers",fr,default);
var fm=await farmService.MutateAsync("flowers","save_draft",fr with {PreviewSignature=fp.Value!.PreviewSignature},default);
Check(fm.Succeeded && fm.Value!.Definition!.Draft.Crops[1].DisplayName=="Emberroot proof","save and reload selected crop");
fr=new(fm.Value!.Definition!.Draft,fm.Value.Definition.UpdatedAtUtc,null,"publish");fp=await farmService.PreviewAsync("flowers",fr,default);
Check((await farmService.MutateAsync("flowers","publish",fr with {PreviewSignature=fp.Value!.PreviewSignature},default)).Succeeded,"publish crop edit");
await using var db=NpgsqlDataSource.Create(connection);
foreach(var item in new[]{"crushed_emberroot","defence_potion_2dose","stoneleaf_seed"})
{
    await using var command=db.CreateCommand("UPDATE item_definitions SET runtime_enabled=false WHERE item_id=@id");command.Parameters.AddWithValue("id",item);
    try {await command.ExecuteNonQueryAsync();throw new Exception("Nested reference was disabled: "+item);}catch(PostgresException e)when(e.SqlState=="P0001"){}
}
Check(publisher.Scopes.Contains(RuntimeCatalogPublicationScope.Alchemy) && publisher.Scopes.Contains(RuntimeCatalogPublicationScope.Farming),"existing catalog publication scopes used");
Console.WriteLine("PASS: Studio authored row preview/save/reload/publish and nested item dependency guards.");
static void Check(bool value,string message){if(!value)throw new Exception(message);}
sealed class RecordingPublisher:IRuntimeCatalogPublisher
{
    public List<RuntimeCatalogPublicationScope> Scopes=[];
    public Task<IReadOnlyList<ApiError>> PublishCatalogsAsync(RuntimeCatalogPublicationScope scope,CancellationToken ct){Scopes.Add(scope);return Task.FromResult<IReadOnlyList<ApiError>>([]);}
}
