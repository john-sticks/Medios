---
name: feedback-deploy-process
description: Deployment is via GitHub Actions on the staging branch — never build/deploy manually via SSH
metadata:
  type: feedback
---

Push changes to the `staging` branch and GitHub Actions handles the deploy automatically.

**Why:** There is a CI/CD action configured for the staging branch that builds and deploys the app.

**How to apply:** After making code changes, commit and push to `staging`. Do NOT manually run `dotnet publish`, `docker build`, or `docker compose up` via SSH — the action does all of that. SSH is only needed for DB migrations or emergency fixes.
