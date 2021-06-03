﻿using System;
using VariantAnnotation.GeneFusions.SA;
using Xunit;

namespace UnitTests.VariantAnnotation.GeneFusions.SA
{
    public sealed class GeneFusionSourceUtilitiesTests
    {
        [Theory]
        [InlineData(GeneFusionSource.Babiceanu_NonCancerTissues,  "Babiceanu non-cancer tissues")]
        [InlineData(GeneFusionSource.Bailey_pancreatic_cancers,   "Bailey pancreatic cancers")]
        [InlineData(GeneFusionSource.Bao_gliomas,                 "Bao gliomas")]
        [InlineData(GeneFusionSource.CACG,                        "CACG")]
        [InlineData(GeneFusionSource.ConjoinG,                    "ConjoinG")]
        [InlineData(GeneFusionSource.COSMIC,                      "COSMIC")]
        [InlineData(GeneFusionSource.Duplicated_Genes_Database,   "Duplicated Genes Database")]
        [InlineData(GeneFusionSource.GTEx_healthy_tissues,        "GTEx healthy tissues")]
        [InlineData(GeneFusionSource.Healthy,                     "Healthy")]
        [InlineData(GeneFusionSource.Healthy_prefrontal_cortex,   "Healthy prefrontal cortex")]
        [InlineData(GeneFusionSource.Human_Protein_Atlas,         "Human Protein Atlas")]
        [InlineData(GeneFusionSource.NonTumorCellLines,           "non-tumor cell lines")]
        [InlineData(GeneFusionSource.Robinson_prostate_cancers,   "Robinson prostate cancers")]
        [InlineData(GeneFusionSource.TCGA_Normal,                 "TCGA normal")]
        [InlineData(GeneFusionSource.TCGA_oesophageal_carcinomas, "TCGA oesophageal carcinomas")]
        [InlineData(GeneFusionSource.TCGA_Tumor,                  "TCGA tumor")]
        public void Convert_ExpectedResults(GeneFusionSource source, string expectedResult)
        {
            string actualResult = GeneFusionSourceUtilities.Convert(source);
            Assert.Equal(expectedResult, actualResult);
        }
        
        [Fact]
        public void Convert_UnknownSource_ThrowException()
        {
            Assert.Throws<NotImplementedException>(delegate
            {
                GeneFusionSourceUtilities.Convert(GeneFusionSource.None);
            });
        }
    }
}