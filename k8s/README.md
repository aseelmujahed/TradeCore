# TradeCore Kubernetes manifests

This directory contains the local Kubernetes deployment resources. They are designed for one API pod and one Trading Engine pod; see [the Kubernetes runbook](../docs/KUBERNETES.md) for the required deployment order and verification procedure.

`secret.example.yaml` is a template only. Do not apply it with placeholder values. Create `secret.local.yaml` from it or create `tradecore-credentials` with `kubectl`; the local file is ignored by Git.
