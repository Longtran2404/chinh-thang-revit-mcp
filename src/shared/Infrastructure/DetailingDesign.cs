// Chinh Thang: audited, bounded TCVN calculations. Never infer design strengths.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RvtMcp.Plugin
{
    public static class DetailingDesign
    {
        private static double RoundUp(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value)||value>100000000)
                throw new ArgumentException("Calculated length is outside the supported finite range.");
            // Suppress sub-nanometre floating point noise at exact 5 mm boundaries.
            return Math.Ceiling((value-1e-9)/5)*5;
        }
        public static double Required(JObject o, string key)
        {
            var t = o[key];
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer))
                throw new ArgumentException(key + " must be an explicit number.");
            double v = (double)t;
            if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) throw new ArgumentException(key + " must be finite and positive.");
            return v;
        }

        public static JObject Calculate(JObject p)
        {
            if (p.Value<string>("standard") == "EN1992-1-1:2004") return Eurocode(p);
            if (p.Value<string>("standard") != "TCVN5574:2018")
                throw new ArgumentException("Select an implemented standard: TCVN5574:2018 or EN1992-1-1:2004. Other editions/codes must be verified before numerical results are enabled.");
            var operation = p.Value<string>("operation");
            if (operation != "Anchorage" && operation != "Lap") throw new ArgumentException("operation must be Anchorage or Lap.");
            var stress = p.Value<string>("steel_stress");
            if (stress != "Tension" && stress != "Compression") throw new ArgumentException("Explicit steel_stress required.");
            var surface = p.Value<string>("surface");
            double eta1;
            if (surface == "HotRolledRibbed") eta1 = 2.5;
            else if (surface == "ColdWorkedRibbed") eta1 = 2;
            else if (surface == "Plain") eta1 = 1.5;
            else throw new ArgumentException("surface must be HotRolledRibbed, ColdWorkedRibbed or Plain; prestressing is unsupported.");
            var shape = p.Value<string>("end_shape");
            if (shape != "Straight" && shape != "L" && shape != "U" && shape != "Hook") throw new ArgumentException("Explicit end_shape required.");
            if (surface == "Plain" && (shape == "Straight" || shape == "L")) throw new ArgumentException("Plain bars require Hook or U in this implementation (10.3.5.2).");
            if (stress == "Compression" && shape != "Straight") throw new ArgumentException("Bent compression anchorage requires a separate reviewed design; no automatic result.");
            double d = Required(p,"diameter_mm"), rs = Required(p,"rs_mpa"), rbt = Required(p,"rbt_mpa");
            double ac = Required(p,"as_required_mm2"), ae = Required(p,"as_provided_mm2");
            if (d > 80 || (d > 32 && d < 36)) throw new ArgumentException("Diameter outside the explicitly implemented diameter bands.");
            if (ac > ae) throw new ArgumentException("Provided reinforcement area is less than required area.");
            if (operation == "Lap" && d > 40) throw new ArgumentException("Lap splices above 40 mm are outside 10.3.6.2.");
            var concrete = p.Value<string>("concrete_kind");
            if (concrete != "NormalWeight" && concrete != "FineGrainedA") throw new ArgumentException("Explicit concrete_kind NormalWeight or FineGrainedA required.");
            var eta2 = d <= 32 ? 1.0 : 0.9;
            var bond = eta1 * eta2 * rbt;
            var basic = rs * d / (4 * bond);
            double alpha = stress == "Tension" ? 1 : 0.75;
            if (operation == "Lap")
            {
                alpha = stress == "Tension" ? 1.2 : 0.9;
                double percent = Required(p,"spliced_percent");
                if (percent > 100) throw new ArgumentException("spliced_percent must not exceed 100.");
                var threshold = surface == "Plain" ? 25.0 : 50.0;
                if (stress == "Tension" && percent > threshold) alpha = 1.2 + 0.8 * (percent-threshold)/(100-threshold);
                if (concrete != "NormalWeight") throw new ArgumentException("Fine-grained concrete lap splices require separate verification; no automatic result.");
            }
            double calculated = alpha * basic * ac / ae;
            if (concrete == "FineGrainedA")
            {
                var state = p.Value<string>("concrete_stress");
                if (state != "Tension" && state != "Compression") throw new ArgumentException("FineGrainedA needs explicit concrete_stress.");
                calculated += (state == "Tension" ? 10 : 5) * d;
            }
            double minimum = operation == "Anchorage" ? Math.Max(200, Math.Max(15*d,0.3*basic)) : Math.Max(250, Math.Max(20*d,0.4*alpha*basic));
            double raw = Math.Max(calculated,minimum);
            double bend = surface == "Plain" ? (d < 20 ? 2.5 : 4)*d : (d < 20 ? 5 : 8)*d;
            return new JObject {
                ["standard"]="TCVN5574:2018",["operation"]=operation,
                ["clauses"]=new JArray("10.3.5.2", "10.3.5.4 (255,256)",operation=="Anchorage" ? "10.3.5.5 (257)" : "10.3.6.2 (259)","10.3.7"),
                ["inputs"]=p.DeepClone(),["eta1"]=eta1,["eta2"]=eta2,["alpha"]=alpha,["rbond_mpa"]=bond,
                ["basic_length_mm"]=basic,["calculated_length_mm"]=calculated,["minimum_length_mm"]=minimum,
                ["required_length_mm"]=RoundUp(raw),["round_up_mm"]=5,["minimum_bend_diameter_mm"]=bend,
                ["reduction_applied"]=false,["scope"]="Non-prestressed bars; calculation of length only, not a complete joint/design verification.",
                ["remaining_checks"]=operation=="Lap" ? new JArray("Transverse reinforcement force capacity","Adjacent lap spacing and clear distances","Splice grouping within 1.3Llap","Cover, confinement and construction specification") : new JArray("Critical section and available embedment geometry","Cover, confinement and concrete splitting","Actual bar type bend diameter","Project-specific loading and material design strengths")
            };
        }

        private static JObject Eurocode(JObject p)
        {
            // Bounded no-reduction implementation. Do not silently apply the 2004 rules to the 2023 edition.
            string op=p.Value<string>("operation"),stress=p.Value<string>("steel_stress"),shape=p.Value<string>("end_shape");
            if(op!="Anchorage"&&op!="Lap")throw new ArgumentException("operation must be Anchorage or Lap.");
            if(stress!="Tension"&&stress!="Compression")throw new ArgumentException("Explicit steel_stress required.");
            if(shape!="Straight"&&shape!="L"&&shape!="U"&&shape!="Hook")throw new ArgumentException("Explicit end_shape required.");
            if(stress=="Compression"&&shape!="Straight")throw new ArgumentException("No bent compression anchorage is credited.");
            if(p.Value<string>("surface")!="HotRolledRibbed"&&p.Value<string>("surface")!="ColdWorkedRibbed")throw new ArgumentException("This EC2 profile covers ribbed, non-prestressed reinforcement only.");
            if(p.Value<string>("concrete_kind")!="NormalWeight")throw new ArgumentException("This EC2 profile covers normal-weight concrete only.");
            if(string.IsNullOrWhiteSpace(p.Value<string>("national_annex")))throw new ArgumentException("Declare the project national_annex/design basis; strengths must already include its design factors.");
            double diameter=Required(p,"diameter_mm"),sigma=Required(p,"sigma_sd_mpa"),fyd=Required(p,"fyd_mpa"),fctd=Required(p,"fctd_mpa");
            if(diameter>32)throw new ArgumentException("This verified EC2 profile is limited to nominal diameters <=32 mm.");
            if(sigma>fyd)throw new ArgumentException("Design steel stress exceeds fyd.");
            string bond=p.Value<string>("bond_condition");
            if(bond!="Good"&&bond!="Poor")throw new ArgumentException("Declare bond_condition Good/Poor according to 8.4.2.");
            double eta1=bond=="Good"?1:0.7;
            double fbd=2.25*eta1*fctd,basic=diameter*sigma/(4*fbd),alpha6=1;
            if(op=="Lap") {
                double percent=Required(p,"spliced_percent");if(percent>100)throw new ArgumentException("spliced_percent must be <=100.");
                alpha6=Math.Min(1.5,Math.Max(1,Math.Sqrt(percent/25)));
            }
            double calculated=basic*alpha6;
            double minimum=op=="Anchorage"?Math.Max((stress=="Tension"?0.3:0.6)*basic,Math.Max(10*diameter,100)):Math.Max(0.3*alpha6*basic,Math.Max(15*diameter,200));
            return new JObject {
                ["standard"]="EN1992-1-1:2004",["operation"]=op,["inputs"]=p.DeepClone(),["clauses"]=new JArray("8.3 / Table 8.1N","8.4.2","8.4.3",op=="Anchorage"?"8.4.4":"8.7.3"),
                ["eta1"]=eta1,["eta2"]=1,["alpha1_to_alpha5"]=1,["alpha6"]=alpha6,["rbond_mpa"]=fbd,["basic_length_mm"]=basic,["calculated_length_mm"]=calculated,["minimum_length_mm"]=minimum,
                ["required_length_mm"]=RoundUp(Math.Max(calculated,minimum)),["minimum_bend_diameter_mm"]=(diameter<=16?4:7)*diameter,["reduction_applied"]=false,["round_up_mm"]=5,
                ["scope"]="2004 edition, static non-prestressed ribbed bars <=32mm, normal-weight concrete, all anchorage reduction factors held at 1.0.",
                ["remaining_checks"]=new JArray("Project national annex and factored material strengths","Bond condition, cover, confinement and concrete failure at bends","Transverse reinforcement, splice staggering and permitted splice zones","Actual critical sections and available embedment geometry")
            };
        }
    }
}
