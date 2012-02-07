﻿using NUnit.Framework;
using NHaml4.Parser.Rules;
using NHaml4.IO;
using System;
namespace NHaml4.Tests.Parser.Rules
{
    [TestFixture]
    public class HamlNodeTextContainer_Tests
    {
        [Test]
        [TestCase("Test", false)]
        [TestCase("   ", true)]
        [TestCase("\n", true)]
        [TestCase("\t", true)]
        public void IsWhitespace_ReturnsCorrectResult(string whiteSpace, bool expectedResult)
        {
            var node = new HamlNodeTextContainer(new HamlLine(whiteSpace, 0));
            Assert.That(node.IsWhitespace(), Is.EqualTo(expectedResult));
        }

        [Test]
        [TestCase("Text", typeof(HamlNodeTextLiteral))]
        [TestCase("#{Variable}", typeof(HamlNodeTextVariable))]
        [TestCase("#{V}", typeof(HamlNodeTextVariable))]
        [TestCase("#{}", typeof(HamlNodeTextLiteral))]
        [TestCase("#{", typeof(HamlNodeTextLiteral))]
        [TestCase("}", typeof(HamlNodeTextLiteral))]
        public void Children_FirstChildIsOfCorrectType(string line, Type expectedType)
        {
            var node = new HamlNodeTextContainer(new HamlLine(line, 0));
            Assert.That(node.Children[0], Is.InstanceOf(expectedType));
        }

        public void Children_EmptyString_NoChildren()
        {
            var node = new HamlNodeTextContainer(new HamlLine("", 0));
            Assert.That(node.Children.Count, Is.EqualTo(0));
        }

        [Test]
        [TestCase("Text#{Variable}", typeof(HamlNodeTextLiteral), typeof(HamlNodeTextVariable))]
        [TestCase("#{Variable}Text", typeof(HamlNodeTextVariable), typeof(HamlNodeTextLiteral))]
        [TestCase("#{Variable1}#{Variable}", typeof(HamlNodeTextVariable), typeof(HamlNodeTextVariable))]
        public void Children_MultipleFragments_ChildrenAreOfCorrectType(string line, Type node1Type, Type node2Type)
        {
            var node = new HamlNodeTextContainer(new HamlLine(line, 0));
            Assert.That(node.Children[0], Is.InstanceOf(node1Type));
            Assert.That(node.Children[1], Is.InstanceOf(node2Type));
        }
    }
}
