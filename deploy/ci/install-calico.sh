#!/usr/bin/env bash
# Installs Calico as the network plugin of a kind cluster created from kind-config.yaml.
#
# kindnet, the default plugin of kind, enforces NetworkPolicies without connection tracking for DNS: with an
# Ingress default deny, the DNS answers sent back to a pod are dropped as new inbound traffic, so no preview
# pod can resolve a name. Calico tracks connections like the plugins of real clusters (k3s ships kube-router).
set -euo pipefail

version="v3.33.0"
sha256="2de8f47595fb9c41b3f47d7b767a1f8e72ecf84057af834738ff12689a234da5"

manifest="$(mktemp)"
trap 'rm -f "$manifest"' EXIT
curl --fail --silent --show-error --location --output "$manifest" \
  "https://raw.githubusercontent.com/projectcalico/calico/${version}/manifests/calico.yaml"
echo "${sha256}  ${manifest}" | sha256sum -c - > /dev/null

kubectl apply --server-side --filename "$manifest" > /dev/null
kubectl --namespace kube-system rollout status daemonset/calico-node --timeout 4m
kubectl wait node --all --for condition=Ready --timeout 2m
