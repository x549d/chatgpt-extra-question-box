using ContextNotes.SelectionProbe;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

// Explicit synthetic-only acceptance of the shipped DLL through its existing
// test friend assembly. No production database, AI service or user profile opens.
if(args.Length!=1) throw new ArgumentException("Pass a new artifacts/phase7-deletion-* directory.");
string root=Path.GetFullPath(args[0]);
string allowed=Path.GetFullPath("artifacts").TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
if(!root.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("phase7-deletion-",StringComparison.Ordinal)||Directory.Exists(root))
    throw new ArgumentException("Refusing an existing directory or a target outside the synthetic acceptance root.");
Directory.CreateDirectory(root);
string path=Path.Combine(root,"synthetic.db"),pre=Path.Combine(root,"before-purge.db"),post=Path.Combine(root,"after-purge.db");
var checks=new List<string>();
void Require(bool ok,string name){if(!ok)throw new InvalidOperationException("FAIL "+name);checks.Add(name);Console.WriteLine("PASS "+name);}
(int,int) Counts(HistoryDatabase db)=>(int.Parse(db.Query("SELECT COUNT(*) FROM annotations")[0][0]!),int.Parse(db.Query("SELECT COUNT(*) FROM messages")[0][0]!));
string Survivors(HistoryDatabase db)=>JsonSerializer.Serialize(new {
    notes=db.Query("SELECT * FROM annotations WHERE id NOT IN ('target-a1','target-a2') ORDER BY id"),
    messages=db.Query("SELECT * FROM messages WHERE annotation_id NOT IN ('target-a1','target-a2') ORDER BY id"),
    settings=db.Query("SELECT * FROM settings ORDER BY key")});
void Healthy(HistoryDatabase db,string stage)=>Require(db.Query("PRAGMA integrity_check")[0][0]=="ok"&&db.Query("PRAGMA foreign_key_check").Count==0,stage);
AnnotationItem Make(string id,string scope,int turns){
    var bounds=new ScreenRectangle(0,0,800,600);var quote=new ScreenRectangle(10,10,100,20);
    var item=new AnnotationItem(id,new SelectionSnapshot(default,bounds,bounds,quote,[quote],"Phase 7 synthetic quote "+id,DateTimeOffset.UtcNow));
    item.AccountScopeId=scope;item.ThreadId="synthetic-thread-"+id;
    for(int t=0;t<turns;t++){item.AddUserMessage("synthetic question "+t);var answer=item.BeginAssistantMessage("synthetic-turn-"+id+"-"+t);item.UpdateAssistantMessage(answer,"synthetic answer "+t,AnnotationGenerationState.Completed,true);}
    return item;
}
string survivors;
using(var db=new HistoryDatabase(path)){
    foreach(var item in new[]{Make("target-a1","synthetic-a",2),Make("target-a2","synthetic-a",1),Make("active-a","synthetic-a",2),Make("archived-a","synthetic-a",1),Make("deleted-b","synthetic-b",2)})db.Save(HistoryRecord.Capture(item));
    db.SetSetting("acceptance-setting","preserve");
    db.SetFlags("target-a1","synthetic-a",true,true);db.SetFlags("target-a2","synthetic-a",false,true);
    db.SetFlags("archived-a","synthetic-a",true,false);db.SetFlags("deleted-b","synthetic-b",false,true);
    Require(Counts(db)==(5,16),"fixture contains five notes and sixteen messages");
    Require(db.Search("synthetic-a",deleted:true).Count==2&&db.Search("synthetic-a",archive:1).Count==1,"archive and trash are distinct");
    Require(db.Query("PRAGMA foreign_keys")[0][0]=="1"&&db.Query("PRAGMA journal_mode")[0][0]=="wal","foreign keys and WAL enabled");
    Require(File.Exists(path+"-wal")&&File.Exists(path+"-shm"),"live WAL and SHM observed");Healthy(db,"initial integrity and foreign keys");
    db.Backup(pre);
    bool refused=false;try{db.PurgeDeleted("synthetic-a",false);}catch(InvalidOperationException){refused=true;}
    Require(refused&&Counts(db)==(5,16),"unconfirmed purge cannot change rows");
    db.PurgeDeleted("synthetic-missing-account",true);Require(Counts(db)==(5,16),"unrelated account purge is harmless");
    survivors=Survivors(db);
    db.Execute("CREATE TRIGGER acceptance_abort BEFORE DELETE ON annotations WHEN OLD.id='target-a2' BEGIN SELECT RAISE(ABORT,'synthetic transaction failure'); END");
    bool rolledBack=false;try{db.PurgeDeleted("synthetic-a",true);}catch(IOException){rolledBack=true;}
    Require(rolledBack&&Counts(db)==(5,16),"injected failure rolls back notes and cascaded messages");Healthy(db,"rollback integrity and foreign keys");
    db.Execute("DROP TRIGGER acceptance_abort");
    using(var reader=new HistoryDatabase(path)){
        reader.Execute("BEGIN");Require(Counts(reader)==(5,16),"reader holds original WAL snapshot");
        db.PurgeDeleted("synthetic-a",true);Require(Counts(db)==(3,10),"account purge removes two notes and six messages");
        Require(Counts(reader)==(5,16),"concurrent reader sees complete pre-delete transaction");
        reader.Execute("COMMIT");Require(Counts(reader)==(3,10),"reader sees committed result after ending snapshot");
    }
    Require(db.Query("SELECT COUNT(*) FROM messages WHERE annotation_id IN ('target-a1','target-a2')")[0][0]=="0","cascade leaves no target messages");
    Require(Survivors(db)==survivors,"active archived other-account rows and settings remain identical");Healthy(db,"post-purge integrity and foreign keys");
    db.Backup(post);Require(db.Query("PRAGMA wal_checkpoint(TRUNCATE)")[0][0]=="0","WAL checkpoint succeeds after readers release snapshot");
}
using(var reopened=new HistoryDatabase(path)){
    Require(Counts(reopened)==(3,10)&&reopened.Search("synthetic-a",deleted:true).Count==0,"fresh connection cannot resurrect deleted rows");
    Require(Survivors(reopened)==survivors,"survivors unchanged across connection restart");Healthy(reopened,"reopened integrity and foreign keys");
}
string oldRestore=Path.Combine(root,"explicit-old-backup-restore.db"),newRestore=Path.Combine(root,"post-delete-restore.db");
HistoryDatabase.BackupFile(pre,oldRestore);HistoryDatabase.BackupFile(post,newRestore);
using(var restored=new HistoryDatabase(oldRestore))Require(Counts(restored)==(5,16),"explicit pre-delete backup restore retains independent old rows");
using(var restored=new HistoryDatabase(newRestore)){
    Require(Counts(restored)==(3,10)&&restored.Search("synthetic-a",deleted:true).Count==0,"post-delete backup restore does not resurrect targets");Healthy(restored,"restored integrity and foreign keys");
}
var result=new{status="PASS",checks=checks.Count,failed=0,skipped=0,database=path,before=new{annotations=5,messages=16},after=new{annotations=3,messages=10},runtime_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(HistoryDatabase).Assembly.Location))),production_database_opened=false,assertions=checks};
File.WriteAllText(Path.Combine(root,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"PASS deletion_acceptance_checks={checks.Count} production_database_opened=false root={root}");
