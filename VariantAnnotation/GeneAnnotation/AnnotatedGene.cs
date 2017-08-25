﻿using System.Text;
using VariantAnnotation.Interface.GeneAnnotation;
using VariantAnnotation.Interface.IO;
using VariantAnnotation.IO;

namespace VariantAnnotation.GeneAnnotation
{
    public class AnnotatedGene:IAnnotatedGene
    {
        public string GeneName { get; }
        public IGeneAnnotation[] Annotations { get; }

        public AnnotatedGene(string geneName, IGeneAnnotation[] annotations)
        {
            GeneName = geneName;
            Annotations = annotations;
        }

        public void SerializeJson(StringBuilder sb)
        {
            var jsonObject = new JsonObject(sb);
            sb.Append(JsonObject.OpenBrace);
            jsonObject.AddStringValue("name",GeneName);
            foreach (var geneAnnotation in Annotations)
            {
                if (geneAnnotation.IsArray)
                {
                    jsonObject.AddStringValues(geneAnnotation.DataSource, geneAnnotation.JsonStrings,false);
                }
                else
                {
                    jsonObject.AddStringValue(geneAnnotation.DataSource, geneAnnotation.JsonStrings[0],false);
                }
                
            }
            
            sb.Append(JsonObject.CloseBrace);
        }

        public void Write(IExtendedBinaryWriter writer)
        {
            writer.Write(GeneName);
            writer.WriteOpt(Annotations.Length);
            for (int i = 0; i < Annotations.Length; i++)
                Annotations[i].Write(writer);
        }

        public int CompareTo(IAnnotatedGene other)
        {
            return GeneName.CompareTo(other.GeneName);
        }
    }
}