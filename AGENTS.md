# MediBridge Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-04-23

## Active Technologies

- C# / .NET 8 + ASP.NET Core Web API, middleware pipeline, Swashbuckle.AspNetCore, JWT Bearer configuration primitives, SQL Server persistence through Entity Framework Core in `MediBridge.Repository` (001-backend-phase1, 002-identity-approval)

## Project Structure

```text
backend/
frontend/
tests/
```

## Commands

# Add commands for C# / .NET 8

## Code Style

C# / .NET 8: Follow standard conventions

Persistence: SQL Server is the required database platform. Entity Framework Core belongs in `MediBridge.Repository` only, behind Repository + Unit of Work abstractions. Do not reference EF Core infrastructure types from `MediBridge.Core` or API controllers.

## Recent Changes

- 001-backend-phase1: Added C# / .NET 8 + ASP.NET Core Web API, middleware pipeline, Swashbuckle.AspNetCore, JWT Bearer configuration primitives

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
<!-- SPECKIT END -->
