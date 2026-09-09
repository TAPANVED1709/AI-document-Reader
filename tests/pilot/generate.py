"""Authored synthetic truth; never imports application extraction/parser code.

Rebuild with python tests/pilot/generate.py. reportlab is a fixture-only dependency.
Range limits here are invented printed examples, not clinical reference guidance.
"""
from copy import deepcopy
from decimal import Decimal
from pathlib import Path
import hashlib
import io
import json

import fitz
from PIL import Image, ImageFilter, ImageEnhance
from reportlab.pdfgen import canvas

HERE = Path(__file__).resolve().parent


def row(name, value, unit, low=None, high=None, *, normalized=None, panel, section,
        ref=None, typ=None, op=None, method=None, flag=None, review=False, normal_unit=None):
    numeric = str(value) if isinstance(value, (int, float, Decimal)) else None
    ref = ref if ref is not None else f"{low} - {high}" if low is not None and high is not None else None
    typ = typ or ("BETWEEN" if low is not None and high is not None else "TEXT_ONLY" if ref else "UNKNOWN")
    status = "UNKNOWN"
    if numeric is not None and typ not in {"UNKNOWN", "TEXT_ONLY"}:
        n = Decimal(numeric)
        status = "LOW" if low is not None and (n < Decimal(str(low)) or n == Decimal(str(low)) and op == ">") else "HIGH" if high is not None and (n > Decimal(str(high)) or n == Decimal(str(high)) and op == "<") else "NORMAL"
    review = review or typ == "UNKNOWN" or (numeric is not None and unit is None)
    return dict(originalTestName=name, normalizedTestName=normalized or name, valueNumeric=numeric,
                valueText=str(value), valueOperator=None, originalUnit=unit, normalizedUnit=normal_unit or unit,
                unit=normal_unit or unit, referenceText=ref, referenceType=typ,
                referenceMin=None if low is None else str(low), referenceMax=None if high is None else str(high),
                referenceOperator=op, reportedFlag=flag, calculatedStatus=status, sectionName=section,
                methodText=method, reviewRequired=review, reviewState="REVIEW_REQUIRED" if review else "AUTO_ACCEPTED",
                panel=panel)


def catalog():
    # Explicit names/units/status inputs, independent of parser output.
    groups = {}
    def add(panel, section, specs):
        groups[panel] = [row(*s[:5], panel=panel, section=section, **(s[5] if len(s)>5 else {})) for s in specs]
    add("CBC", "Hematology", [
        ("Hemoglobin",10.8,"g/dL",13.0,17.0,{"flag":"L"}),
        ("WBC",6.2,"K/uL",4.0,11.0,{"normal_unit":"10³/µL"}),
        ("Platelet Count",210,"K/uL",150,400,{"normal_unit":"10³/µL"}),
        ("Hematocrit",36.2,"%",35,50), ("MCV",83.1,"fL",80,100),
        ("MCH",28.4,"pg",27,33), ("MCHC",33.2,"g/dL",31,36),
        ("Neutrophils",62,"%",40,75), ("Lymphocytes",29,"%",20,45),
        ("Monocytes",6,"%",2,10), ("Eosinophils",2,"%",1,6), ("Basophils",1,"%",0,2)])
    add("Liver", "Liver Function Test", [
        ("AST",32,"U/L",10,40,{"method":"IFCC"}), ("ALT",41,"U/L",7,45),
        ("ALP",88,"U/L",40,130), ("GGT",34,"U/L",8,61),
        ("Total Bilirubin",1.1,"mg/dL",0.2,1.2), ("Direct Bilirubin",0.3,"mg/dL",0.0,0.4),
        ("Albumin",4.2,"g/dL",3.5,5.2), ("Total Protein",7.1,"g/dL",6.0,8.3)])
    add("Kidney", "Kidney Function Test", [
        ("Creatinine",0.9,"mg/dL",0.7,1.3,{"method":"Enzymatic"}),
        ("Urea",29,"mg/dL",15,40), ("BUN",14,"mg/dL",7,20), ("Uric Acid",5.8,"mg/dL",3.5,7.2)])
    add("Diabetes", "Diabetes Profile", [
        ("Fasting Glucose",92,"mg/dL",70,99,{"normalized":"Glucose","method":"Hexokinase"}),
        ("HbA1c",6.8,"%",4.0,5.6,{"method":"HPLC","flag":"H"})])
    add("Lipids", "Lipid Profile", [
        ("Total Cholesterol",184,"mg/dL",None,200,{"ref":"< 200","typ":"LESS_THAN","op":"<"}),
        ("HDL",52,"mg/dL",40,None,{"ref":">= 40","typ":"GREATER_THAN_OR_EQUAL","op":">="}),
        ("LDL",103,"mg/dL",None,130,{"ref":"<= 130","typ":"LESS_THAN_OR_EQUAL","op":"<="}),
        ("Triglycerides",138,"mg/dL",40,150)])
    add("Thyroid", "Thyroid Profile", [("TSH",2.1,"uIU/mL",0.4,4.0,{"normal_unit":"µIU/mL"}), ("Free T4",1.2,"ng/dL",0.8,1.8)])
    add("Iron", "Iron Studies", [("Iron",72,"ug/dL",50,170,{"normal_unit":"µg/dL"}), ("Ferritin",48,"ng/mL",15,150), ("TIBC",310,"ug/dL",250,450,{"normal_unit":"µg/dL"})])
    add("Vitamins", "Vitamin Profile", [("Vitamin B12",420,"pg/mL",200,900), ("Vitamin D",24,"ng/mL",20,50), ("Folate",9.1,"ng/mL",4,20)])
    add("Inflammation", "Inflammation", [("CRP",3.2,"mg/L",None,5,{"ref":"< 5","typ":"LESS_THAN","op":"<"}), ("ESR",12,"mm/hr",0,20)])
    add("Coagulation", "Coagulation", [("PT",12.4,"sec",10,14,{"method":"Optical"}), ("INR",1.0,"ratio",0.8,1.2), ("aPTT",29,"sec",25,35)])
    add("Urine", "Urine Examination", [("Protein","Negative",None,None,None,{"ref":"Negative"}), ("Ketones","Trace",None,None,None,{"ref":"Absent"}), ("Nitrite","Nil",None,None,None,{"ref":"Nil"}), ("Leukocyte Esterase","Present",None,None,None,{"ref":"Absent"})])
    add("Electrolytes", "Electrolytes", [("Sodium",167,"mmol/L",135,145,{"flag":"H"}), ("Potassium",4.1,"mmol/L",3.5,5.1), ("Chloride",101,"mmol/L",98,107), ("Ionized Calcium",1.9,"mmol/L",1.1,1.3)])
    add("Serology / Immunology", "Serology", [("HBsAg","Non-Reactive",None,None,None,{"ref":"Non-Reactive","method":"CLIA"}), ("HIV","Negative",None,None,None,{"ref":"Negative"}), ("HCV","Reactive",None,None,None,{"ref":"Non-Reactive"}), ("VDRL","Positive",None,None,None,{"ref":"Negative"})])
    add("Other", "Metabolic", [("Magnesium",2.1,"mg/dL",1.7,2.4), ("Phosphorus",3.4,"mg/dL",2.5,4.5)])
    return groups


def documents():
    c = catalog()
    docs = []
    def add(title, panels, *, mode="Native", layout="list", features=(), meta="", rows=None, pages=1):
        values = deepcopy(rows if rows is not None else [r for p in panels for r in c[p]])
        docs.append(dict(documentId=f"pilot-{len(docs)+1:02d}", title=title, format=mode, layout=layout,
                         tags=[mode]+(["Multi-page"] if pages>1 else [])+(["Multi-column"] if layout in {"table", "parallel"} else [])+(["Low-quality OCR"] if "degraded" in features else []),
                         features=list(features), metadata=meta, expectedPages=pages, expectedDocumentType="LAB_REPORT", expectedResults=values))
    add("Single glucose / hexokinase",[],rows=c["Diabetes"][:1])
    add("Single scanned glucose",[],rows=c["Diabetes"][:1],mode="Scanned")
    add("CBC three-column table",["CBC"],layout="table")
    add("Scanned CBC three-column table",["CBC"],layout="table",mode="Scanned")
    add("Biochemistry continuation",["Diabetes","Kidney","Liver","Electrolytes"],pages=3)
    add("Hybrid biochemistry continuation",["Diabetes","Kidney","Liver","Electrolytes"],pages=3,mode="Hybrid")
    add("Scanned biochemistry",["Kidney","Liver","Electrolytes"],pages=2,mode="Scanned")
    add("Multi-section metabolic and coagulation",["Other","Coagulation","CBC","Diabetes"],pages=3,features=["biochemistry"])
    add("Wrapped names and ranges",["CBC","Liver"],pages=2,features=["wrapped"])
    add("Scanned wrapped rows",["CBC","Kidney"],pages=2,mode="Scanned",features=["wrapped"])
    add("Parallel biochemical panels",["Liver","Kidney","Electrolytes"],layout="parallel")
    add("Parallel hematology and lipid panels",["CBC","Lipids"],layout="parallel",mode="Scanned")
    add("Female printed intervals",["CBC","Kidney"],meta="Age: 32 years   Sex: Female",features=["sex"])
    add("Male printed intervals",["CBC","Kidney"],meta="Age: 42 years   Sex: Male",features=["sex"],mode="Scanned")
    add("Sex unknown printed intervals",["CBC","Kidney"],features=["sex"])
    add("Conflicting sex metadata",["CBC","Kidney"],meta="Sex: Male   Sex: Female",features=["sex","ambiguous"])
    add("Child printed age intervals",["CBC","Kidney"],meta="Age: 8 years",features=["age"])
    add("Infant printed month intervals",["CBC","Kidney"],meta="Age: 8 months",features=["months"],mode="Hybrid",pages=3)
    add("Age unknown printed intervals",["CBC","Kidney"],features=["age"])
    add("Qualitative urine and serology",["Urine","Serology / Immunology","Coagulation"])
    add("Scanned qualitative values",["Urine","Serology / Immunology","Coagulation"],mode="Scanned")
    add("Endocrine and nutrition",["Thyroid","Iron","Vitamins","Lipids","Inflammation"],layout="table")
    add("Hybrid endocrine and nutrition",["Thyroid","Iron","Vitamins","Lipids","Inflammation"],mode="Hybrid",pages=3)
    add("Missing fields retained for review",["Kidney","Liver","Diabetes"],features=["missing"])
    add("Comments and close distinct rows",["CBC","Diabetes","Coagulation"],features=["comments","close"])
    add("Repeated concepts with methods",["Diabetes","Kidney","Liver"],features=["duplicate"],pages=2)
    add("Faint scanned decimals",["Diabetes","Kidney","Liver"],features=["degraded","decimals"],mode="Scanned")
    add("Low resolution scan and character corruption",["CBC","Kidney","Liver"],features=["degraded","characters"],mode="Scanned",pages=2)
    add("Hybrid flags and unusual printed values",["Electrolytes","Liver","Diabetes"],features=["flag-disagreement"],mode="Hybrid",pages=3)
    add("Scanned multipanel continuation",["Other","Iron","Vitamins","Inflammation","Coagulation","Urine"],mode="Scanned",pages=3,features=["comments"])
    add("Decimal loss triplet and absent qualitative control",[],mode="Scanned",features=["degraded","decimals"],rows=[c["CBC"][0],c["Diabetes"][1],c["Kidney"][0],row("Protein","Absent",None,ref="Absent",panel="Urine",section="Urine Examination")])
    for d in docs:
        rows=d["expectedResults"]; features=d["features"]
        if "duplicate" in features:
            a=deepcopy(c["Diabetes"][0]); a.update(valueNumeric="94",valueText="94",methodText="Glucose oxidase")
            rows.insert(1,a)
        for i,r in enumerate(rows):
            r.update(rowId=f'{d["documentId"]}-r{i+1:02d}',pageNumber=min(d["expectedPages"],i*d["expectedPages"]//len(rows)+1))
            if "biochemistry" in features and r["panel"] == "Diabetes": r["sectionName"]="Biochemistry"
            if r["originalTestName"]=="Hemoglobin" and any(f in features for f in ["sex","age","months"]):
                if "sex" in features:
                    text="Male: 13.0 - 17.0; Female: 12.0 - 15.0"
                    limits=(12,15) if "Female" in d["metadata"] else (13,17)
                elif "months" in features:
                    text="0-6 months: 9.0 - 13.0; 7-12 months: 10.0 - 14.0"; limits=(10,14)
                else:
                    text="0-12 years: 11.0 - 15.0; Adult: 13.0 - 17.0"; limits=(11,15)
                known=bool(d["metadata"]) and "ambiguous" not in features
                r.update(referenceText=text,referenceType="BETWEEN" if known else "TEXT_ONLY",referenceMin=str(limits[0]) if known else None,referenceMax=str(limits[1]) if known else None,calculatedStatus=("LOW" if Decimal(r["valueNumeric"])<limits[0] else "NORMAL") if known else "UNKNOWN",reportedFlag=None,reviewRequired=not known)
            if "missing" in features and i in (0,2):
                if i==0: r.update(originalUnit=None,normalizedUnit=None,unit=None)
                else: r.update(referenceText=None,referenceMin=None,referenceMax=None,referenceType="UNKNOWN",calculatedStatus="UNKNOWN")
                r["reviewRequired"]=True
            if "flag-disagreement" in features and i==0: r.update(reportedFlag="L",reviewRequired=True)
            if "decimals" in features and r["originalTestName"] in {"Hemoglobin","HbA1c","Creatinine"}:
                r["reviewRequired"]=True; r["ambiguity"]="decimal deliberately faded in scan; clean authored value retained"
            r["reviewState"]="REVIEW_REQUIRED" if r["reviewRequired"] else "AUTO_ACCEPTED"
    return docs


def render(d):
    stream=io.BytesIO(); width,height=(1100,850) if d["layout"]=="parallel" else (900,850)
    cv=canvas.Canvas(stream,pagesize=(width,height),invariant=1)
    cv.setTitle("Synthetic pathology engineering fixture"); cv.setAuthor("Synthetic pilot generator")
    for pn in range(1,d["expectedPages"]+1):
        cv.setFont("Helvetica-Bold",18); cv.drawString(35,height-40,"SYNTHETIC PATHOLOGY LABORATORY")
        cv.setFont("Helvetica",10); cv.drawString(35,height-60,"Engineering fixture - no patient identifiers - not a clinical report")
        if d["metadata"]: cv.drawString(35,height-82,d["metadata"])
        cv.drawRightString(width-35,height-82,f"Page {pn} of {d['expectedPages']}")
        page_rows=[r for r in d["expectedResults"] if r["pageNumber"]==pn]
        y=height-115; previous=None; half=(len(page_rows)+1)//2
        for i,r in enumerate(page_rows):
            x=35
            if d["layout"]=="parallel" and i>=half:
                x=width/2+20
                if i==half: y=height-115; previous=None
            if previous!=r["sectionName"]:
                cv.setFont("Helvetica-Bold",12); cv.drawString(x,y,r["sectionName"]); y-=23; previous=r["sectionName"]
            font=11 if d["layout"]=="parallel" else 13
            cv.setFont("Helvetica",font)
            name=r["originalTestName"]
            if "characters" in d["features"]:
                name=name.replace("Hemoglobin","HemogIobin").replace("Creatinine","Creatlnine").replace("Bilirubin","Bilirubln")
            tail=" ".join(filter(None,[r["valueText"],r["originalUnit"],r["reportedFlag"],r["referenceText"]]))
            method=f" Method: {r['methodText']}" if r["methodText"] else ""
            wrap="wrapped" in d["features"] and name in {"Platelet Count","Total Protein"}
            if wrap:
                prefix,suffix=name.rsplit(" ",1); cv.drawString(x,y,prefix); y-=16; name=suffix
            if d["layout"]=="table":
                cv.drawString(x,y,name); cv.drawString(x+235,y," ".join(filter(None,[r["valueText"],r["originalUnit"],r["reportedFlag"]])))
                cv.drawString(x+440,y,(r["referenceText"] or "")+method)
            elif "wrapped" in d["features"] and name=="MCHC":
                cv.drawString(x,y,f"{name} {r['valueText']} {r['originalUnit']} 31 -"); y-=16; cv.drawString(x+300,y,"36")
            else:
                # Decimal fade affects ink, not truth. The pre-degradation value remains in JSON.
                if "decimals" in d["features"] and r["originalTestName"] in {"Hemoglobin","HbA1c","Creatinine"}:
                    line=name+" "+tail+method
                    pos=line.index("."); before=line[:pos]; after=line[pos+1:]
                    cv.drawString(x,y,before); dx=cv.stringWidth(before,"Helvetica",font)
                    cv.setFillGray(.94); cv.drawString(x+dx,y,"."); cv.setFillGray(0)
                    cv.drawString(x+dx+cv.stringWidth(".","Helvetica",font),y,after)
                else: cv.drawString(x,y,name+" "+tail+method)
            r["sourceRegion"]={"x":x,"y":height-y-(32 if wrap else 16),"width":width/2-55 if d["layout"]=="parallel" else width-70,"height":34 if wrap else 19,"coordinateSpace":"pdf_points"}
            y-=19 if "close" in d["features"] else 28
        if "comments" in d["features"]:
            cv.setFont("Helvetica-Oblique",11)
            for t in ["Sample hemolysed", "Repeat advised", "Fasting sample", "Reference range revised"]:
                cv.drawString(35,y-10,t); y-=18
        assert y>45,(d["documentId"],y)
        cv.showPage()
    cv.save()
    source=fitz.open(stream=stream.getvalue(),filetype="pdf"); out=fitz.open()
    sources=[]
    for i,p in enumerate(source):
        scan=d["format"]=="Scanned" or d["format"]=="Hybrid" and i==1
        if scan:
            dpi=135 if "degraded" in d["features"] else 200
            pix=p.get_pixmap(dpi=dpi,colorspace=fitz.csGRAY)
            im=Image.frombytes("L",(pix.width,pix.height),pix.samples)
            if "degraded" in d["features"]: im=ImageEnhance.Contrast(im.filter(ImageFilter.GaussianBlur(.35))).enhance(.72)
            buf=io.BytesIO(); im.save(buf,format="PNG")
            out.new_page(width=p.rect.width,height=p.rect.height).insert_image(p.rect,stream=buf.getvalue())
        else: out.insert_pdf(source,from_page=i,to_page=i)
        sources.append("OCR" if scan else "NATIVE_TEXT")
    d["expectedPageSources"]=sources
    d["expectedProcessingMode"]={"Native":"NATIVE","Scanned":"OCR","Hybrid":"HYBRID"}[d["format"]]
    data=out.tobytes(deflate=True); out.close(); source.close()
    return data


def main(only_new=False):
    for folder in ["fixtures","ground_truth"]: (HERE/folder).mkdir(parents=True,exist_ok=True)
    manifest=json.loads((HERE/"manifest.json").read_text()) if only_new else []
    for d in documents():
        if only_new and any(m["documentId"]==d["documentId"] for m in manifest): continue
        data=render(d); key=d["documentId"]
        d["privacy"]="Entirely synthetic; no patient name, DOB, address, accession or patient identifier. Age/sex are invented parsing inputs."
        d["truthProvenance"]="Manually specified generator catalog and printed layout; never application output. No truth corrections."
        (HERE/"fixtures"/f"{key}.pdf").write_bytes(data)
        truth=json.dumps(d,indent=2,ensure_ascii=False)+"\n"
        (HERE/"ground_truth"/f"{key}.json").write_bytes(truth.encode("utf-8"))
        manifest.append(dict(documentId=key,pdfSha256=hashlib.sha256(data).hexdigest(),truthSha256=hashlib.sha256(truth.encode()).hexdigest(),rows=len(d["expectedResults"])))
    (HERE/"manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
    print(f"Generated {len(manifest)} documents, {sum(m['rows'] for m in manifest)} rows")


if __name__=="__main__":
    import sys
    main(only_new="--only-new" in sys.argv)
