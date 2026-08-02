#!/usr/bin/env python3
"""Source contracts for compiled host behavioral tests."""

from __future__ import annotations

import unittest
from pathlib import Path
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
PROJECT = (
    ROOT
    / "tests"
    / "host"
    / "MMO.ContentStudio.AuthoringHost.Tests"
    / "MMO.ContentStudio.AuthoringHost.Tests.csproj"
)
TESTS = PROJECT.with_name("ContentCatalogServiceTests.cs")


class CompiledHostTestContracts(unittest.TestCase):
    def test_unit_test_project_targets_host_runtime(self) -> None:
        tree = ET.parse(PROJECT)
        root = tree.getroot()
        text = PROJECT.read_text()
        self.assertIn("<TargetFramework>net10.0</TargetFramework>", text)
        self.assertIn("<OutputType>Exe</OutputType>", text)
        self.assertIn('PackageReference Include="xunit.v3" Version="3.2.2"', text)
        self.assertIn('PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1"', text)
        self.assertIn('ProjectReference Include="../../../host/MMO.ContentStudio.AuthoringHost.csproj"', text)
        self.assertEqual("Project", root.tag)

    def test_catalog_behavior_is_exercised_as_compiled_code(self) -> None:
        source = TESTS.read_text()
        for token in (
            "using Xunit;",
            "TestContext.Current.CancellationToken",
            "LoadAsyncOrdersProvidersBySortOrderThenContentType",
            "LoadAsyncRejectsDuplicateContentTypes",
            "PlannedProviderReturnsUnimplementedEmptySection",
            "PlannedProviderObservesCancellation",
            "new ContentCatalogService",
            "Assert.ThrowsAsync<InvalidOperationException>",
        ):
            self.assertIn(token, source)

    def test_ci_restores_and_runs_host_unit_tests(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "ci.yml").read_text()
        path = "tests/host/MMO.ContentStudio.AuthoringHost.Tests/MMO.ContentStudio.AuthoringHost.Tests.csproj"
        self.assertIn(f"dotnet restore {path}", workflow)
        self.assertIn(f"dotnet test {path}", workflow)

    def test_local_test_command_runs_host_unit_tests_when_dotnet_exists(self) -> None:
        script = (ROOT / "tools" / "test.sh").read_text()
        self.assertIn("MMO.ContentStudio.AuthoringHost.Tests.csproj", script)
        self.assertIn("dotnet test", script)


if __name__ == "__main__":
    unittest.main()
