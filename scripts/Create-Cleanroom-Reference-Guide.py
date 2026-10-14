"""Independently authored, end-to-end SampleApp reference authoring manifest.

No existing Guide or GuideStep is used as authoring input. --apply creates
or updates only this reference Guide. Runtime verification is separate.
"""
import argparse
import json
import sqlite3
from pathlib import Path

DB = r"C:\ProgramData\GuideMe\Data\GuideMe.db"
KEY = "sampleapp-cleanroom-reference"
CUSTOMER = "לקוח מדריך ייחוס"
FRAME = [{"Strategy": "css", "Value": "#content-frame"}]

def step(runtime, key, message, strategy=None, value=None, placement="Auto",
         anchors=(), validation=None, expected=None, completion=(), capture=None,
         advance="AutomaticOnValidation", context=None):
    return dict(runtime=runtime, key="reference-"+key, bubble=message,
                strategy=strategy, value=value, placement=placement,
                anchors=list(anchors), validation=validation, expected=expected,
                completion=list(completion), capture=capture, advance=advance,
                context=context)

def a(relation, strategy, value):
    return (relation, strategy, value)

# New user journey, not extracted from any existing guide.
# A newly created customer is used to demonstrate persistence across runtimes.
STEPS = [
 step("Web","web-intro","ניצור לקוח חדש בדפדפן, נאתר אותו ביישום החלונות ונחזור לבדוק את הנתונים.",advance="Manual",placement="Center"),
 step("Web","web-new-customer","פתח לקוח חדש.", "css", 'button[data-go="#/customer/new"]', "Top",
      completion=[("target-not-exists","css",'button[data-go="#/customer/new"]',None)]),
 step("Web","web-name","הזן שם לקוח חדש: "+CUSTOMER,"css","#customer-form input[name=name]",
      anchors=[a("Ancestor","css","#customer-form")],validation="value-equals",expected=CUSTOMER,placement="Right"),
 step("Web","web-phone","הזן מספר טלפון תקין.","css","#customer-form input[name=phone]",
      validation="value-not-empty",placement="Left"),
 step("Web","web-email","הזן כתובת דוא״ל תקינה.","css","#customer-form input[name=email]",
      validation="value-not-empty",placement="Bottom"),
 step("Web","web-save","שמור את הלקוח.","css","#customer-form button.primary",
      anchors=[a("Ancestor","css","#customer-form")],placement="Top",
      completion=[("target-not-exists","css","#customer-form button.primary",None)]),
 step("Web","web-new-site","צור אתר חדש עבור הלקוח שנשמר.","css",'button[data-go$="/site/new"]',advance="Manual",placement="Right"),
 step("Web","web-site-name","הזן שם אתר חדש.","css","#site-form input[name=name]",
      validation="value-not-empty",placement="Left"),
 step("Web","web-site-type","בחר סוג אתר.","css","#site-form select[name=type]",placement="Bottom",validation="value-not-empty"),
 step("Web","web-site-address","הזן כתובת לאתר.","css","#site-form input[name=address]",
      validation="value-not-empty",placement="Top"),
 step("Web","web-site-save","שמור את האתר.","css","#site-form button.primary",placement="Right",
      completion=[("target-not-exists","css","#site-form button.primary",None)]),
 step("Web","web-cases-tab","פתח את לשונית הפניות.","css",'nav.tabs button[data-go$="/cases"]',advance="Manual",placement="Top"),
 step("Web","web-new-case","פתח פנייה חדשה.","css",'button[data-go$="/case/new"]',advance="Manual",placement="Right"),
 step("Web","web-case-subject","הזן נושא לפנייה.","css","#record-form input[name=subject]",
      validation="value-not-empty",placement="Left"),
 step("Web","web-case-description","הזן תיאור לפנייה.","css","#record-form textarea[name=description]",
      validation="value-not-empty",placement="Bottom"),
 step("Web","web-case-save","שמור את הפנייה.","css","#record-form button.primary",placement="Top",
      completion=[("target-not-exists","css","#record-form button.primary",None)]),
 step("Web","web-handoff","הנתונים נשמרו. עבור ליישום החלונות ופתח את מסך חיפוש הלקוחות.",advance="Manual",placement="Center"),
 step("Windows","windows-name","חפש את הלקוח שיצרת בדפדפן.","automation-id","CustomerNameSearch",
      anchors=[a("Self","name","שם לקוח")],validation="value-equals",expected=CUSTOMER,placement="Right"),
 step("Windows","windows-search","הפעל חיפוש לקוח.","automation-id","SearchCustomersButton",placement="Top",
      completion=[("target-exists","automation-id","CustomersGrid",None)]),
 step("Windows","windows-results","פתח בלחיצה כפולה את רשומת הלקוח בתוצאות.","automation-id","CustomersGrid",placement="Left",
      completion=[("target-exists","automation-id","SitesGrid",None)]),
 step("Windows","windows-sites","פתח בלחיצה כפולה את האתר שנוצר.","automation-id","SitesGrid",placement="Right",
      completion=[("target-exists","automation-id","SiteDetailsTab",None)]),
 step("Windows","windows-cases","פתח את לשונית הפניות.","automation-id","CasesTab",placement="Top",
      completion=[("target-exists","automation-id","CasesGrid",None)]),
 step("Windows","windows-grid","פתח בלחיצה כפולה את הפנייה שנוצרה.","automation-id","CasesGrid",placement="Left",
      completion=[("target-exists","automation-id","CaseStatus",None)]),
 step("Windows","windows-status","שנה את סטטוס הפנייה ל'בטיפול'.","automation-id","CaseStatus",placement="Right",
      validation="value-equals",expected="בטיפול"),
 step("Windows","windows-save","שמור את השינוי בפנייה.","automation-id","SaveCaseButton",advance="Manual",placement="Bottom"),
 step("Windows","windows-handoff","חזור לדפדפן ובדוק את הנתונים שנשמרו.",advance="Manual",placement="Center"),
 step("Web","web-return","חזור לפורטל באמצעות כותרת היישום.","css","#portal-header",placement="Top",
      completion=[("target-exists","css","#customer-search",None)]),
 step("Web","web-search-name","הזן את שם הלקוח שיצרת.","css","#customer-search input[name=name]",
      anchors=[a("Ancestor","css","#customer-search"),a("Ancestor","css","label.field")],
      validation="value-equals",expected=CUSTOMER,placement="Right"),
 step("Web","web-search-submit","הפעל חיפוש.","css","#customer-search button.primary",placement="Bottom",
      completion=[("target-exists","css","#search-results tbody tr.clickable",None)]),
 step("Web","web-results","בדוק את שם הלקוח בתוצאות ורק אז פתח את השורה המתאימה.","css","#search-results tbody tr.clickable",placement="Left",
      completion=[("target-not-exists","css","#search-results tbody tr.clickable",None)]),
 step("Web","web-finish","הגעת לסיום תרחיש הייחוס. ודא שהנתונים מופיעים בשני היישומים.",advance="Manual",placement="Center"),
]

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--db",default=DB)
    p.add_argument("--apply",action="store_true")
    p.add_argument("--manifest",default="cleanroom-reference-manifest.json")
    args=p.parse_args()
    # Static contract: every automatic step must have a meaningful observable
    # transition or value validation. Target presence alone is not completion.
    keys=[item["key"] for item in STEPS]
    if len(keys)!=len(set(keys)):
        p.error("Duplicate step keys")
    for index,item in enumerate(STEPS,1):
        if item["advance"]=="AutomaticOnValidation" and not (item["validation"] or item["completion"]):
            p.error(f"Step {index} has no observable completion: {item['key']}")
        if item["runtime"] not in ("Web","Windows"):
            p.error(f"Invalid runtime in step {index}")
    Path(args.manifest).write_text(json.dumps({"guide":KEY,"status":"AUTHORING_NOT_RUNTIME_VERIFIED",
        "steps":STEPS},ensure_ascii=False,indent=2),encoding="utf-8")
    print(f"New independent workflow: {len(STEPS)} steps; manifest: {args.manifest}")
    path=Path(args.db)
    if not path.is_file(): p.error(f"Database missing: {path}")
    uri=path.resolve().as_uri()+("?mode=rw" if args.apply else "?mode=ro")
    with sqlite3.connect(uri,uri=True,timeout=5) as conn:
        conn.row_factory=sqlite3.Row
        conn.execute("PRAGMA busy_timeout=5000")
        if not args.apply:
            print("PREVIEW ONLY; no DB changes. Live runtime validation is pending.")
            return
        conn.execute("PRAGMA foreign_keys=ON")
        conn.execute("BEGIN IMMEDIATE")
        existing=conn.execute("SELECT Id FROM Guides WHERE Key=?",(KEY,)).fetchone()
        if existing:
            gid=existing["Id"]
            # Replace only this Guide's persisted steps; dependent rows are
            # deleted through the repository's ON DELETE CASCADE relationships.
            conn.execute("DELETE FROM GuideSteps WHERE GuideId=?",(gid,))
            conn.execute("DELETE FROM GuideApplicationContexts WHERE GuideId=?",(gid,))
        else:
            gid=conn.execute("INSERT INTO Guides(Key,Name) VALUES(?,?)",
                             (KEY,"SampleApp Cleanroom Capability Reference")).lastrowid
        for order,s in enumerate(STEPS,1):
            runtime=s["runtime"] if s["strategy"] else None
            frame=json.dumps([{"Strategy":"css","Value":"#header-frame"}] if s["key"]=="reference-web-return" else FRAME) if runtime=="Web" else None
            context=s["context"]
            sid=conn.execute("""INSERT INTO GuideSteps(
                GuideId,Key,StepOrder,AdvanceMode,Runtime,LocatorStrategy,LocatorValue,
                FrameContextJson,ContextKind,ContextValue,BubbleContent,BubblePlacement,
                ValidationKind,ValidationExpectedValue,IsEnabled)
                VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                (gid,s["key"],order,s["advance"],runtime,s["strategy"],s["value"],frame,
                 context[0] if context else None,context[1] if context else None,
                 s["bubble"],s["placement"],s["validation"],s["expected"],1)).lastrowid
            for index,(relation,strategy,value) in enumerate(s["anchors"]):
                conn.execute("""INSERT INTO TargetAnchors(
                    GuideStepId,AnchorOrder,Relation,LocatorStrategy,LocatorValue)
                    VALUES(?,?,?,?,?)""",(sid,index,relation,strategy,value))
            for condition_order,(kind,strategy,value,expected) in enumerate(s["completion"]):
                target={"Runtime":s["runtime"],"Locator":{"Strategy":strategy,"Value":value},
                        "Anchors":[],"FrameContext":{"Path":([{"Strategy":"css","Value":"#header-frame"}] if s["key"]=="reference-web-return" else FRAME)} if s["runtime"]=="Web" else None}
                conn.execute("""INSERT INTO StepCompletionConditions(
                    GuideStepId,ConditionOrder,Kind,ExpectedValue,TargetJson) VALUES(?,?,?,?,?)""",
                    (sid,condition_order,kind,expected,json.dumps(target,ensure_ascii=False)))
        conn.commit()
        print(f"INSTALLED {KEY}: {len(STEPS)} steps. Not runtime verified.")

if __name__=="__main__":
    main()
