#!/bin/bash

echo "🛑 Stopping A3ITranslator API..."
az container stop --name a3itranslator-api --resource-group A3ITranslationRG

echo "💰 Container stopped - billing paused!"
echo "📊 Final status:"
az container show --name a3itranslator-api --resource-group A3ITranslationRG --query "instanceView.state" --output tsv
